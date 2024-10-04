using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using SS14.Watchdog.Components.ProcessManagement;

namespace SS14.Watchdog.Components.ServerManagement;

public sealed partial class ServerInstance
{
    private const int CommandChannelCapacity = 32;

    // Server instance management uses an actor-ish model:
    // the actual server monitoring, command sending, all that logic is done with a single command queue.

    private readonly Channel<Command> _commandQueue = Channel.CreateBounded<Command>(
        new BoundedChannelOptions(CommandChannelCapacity)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    private string _baseServerAddress = "";

    /// <summary>
    /// The last time a ping was received from the game server.
    /// </summary>
    /// <remarks>
    /// This is in local time, not UTC.
    /// </remarks>
    private DateTime? _lastPing;

    private CancellationTokenSource? _serverTimeoutTcs;
    private int _serverTimeoutNumber;

    private int _startNumber;
    // Server got an explicit stop command, will not be automatically restarted.
    private bool _stopped;

    public async Task StartAsync(string baseServerAddress, CancellationToken cancel)
    {
        _baseServerAddress = baseServerAddress;

        _logger.LogDebug("Starting server {Key}", Key);

        await TryLoadPersistedProcess(cancel);
        await _commandQueue.Writer.WriteAsync(new CommandStart(), cancel);

        try
        {
            await CommandLoop(cancel);

            // Currently no way for this to be reachable, as shutdown is always caused by cancellation.
        }
        catch (OperationCanceledException)
        {
            // Nada, expected shutdown sequence.
        }
        catch (Exception e)
        {
            // Oh fuck.
            _logger.LogCritical(e, "Exception occurred in server instance loop!");
        }
        finally
        {
            // Whether clean or unclear, run shutdown logic to the best of our abilities.

            await ShutdownAsync();
        }
    }

    private async Task TryLoadPersistedProcess(CancellationToken cancel)
    {
        if (!_processManager.CanPersist)
            return;

        string? token;
        {
            using var con = _dataManager.OpenConnection();
            token = con.QuerySingle<string?>(
                "SELECT PersistedToken FROM ServerInstance WHERE Key = @Key",
                new { Key });
        }

        if (token == null)
        {
            _logger.LogDebug("No persisted server token, not trying to load persisted process.");
            return;
        }

        _logger.LogInformation("Trying to load persisted server process...");

        var handle = await _processManager.TryGetPersistedServer(this, GetProgramPath(), cancel);
        if (handle == null)
        {
            _logger.LogInformation("Couldn't load persisted process");
            return;
        }

        _logger.LogInformation("Loading persisted process: {Process}", handle);
        _runningServer = handle;

        SetToken(token);

        MonitorServer(++_startNumber, cancel);
    }

    private async Task CommandLoop(CancellationToken cancel)
    {
        _logger.LogDebug("Entering command loop");

        await foreach (var command in _commandQueue.Reader.ReadAllAsync(cancel))
        {
            await RunCommand(command, cancel);
        }
    }

    private async Task RunCommand(Command command, CancellationToken cancel)
    {
        _logger.LogTrace("Running command: {Command}", command);

        switch (command)
        {
            case CommandStart:
                await RunCommandStart(cancel);
                break;
            case CommandServerExit exit:
                await RunCommandServerExit(exit, cancel);
                break;
            case CommandRestart:
                await RunCommandRestart(cancel);
                break;
            case CommandStop stop:
                await RunCommandStop(stop.StopCommand, cancel);
                break;
            case CommandServerPing ping:
                await RunCommandServerPing(ping, cancel);
                break;
            case CommandTimedOut timedOut:
                await RunCommandTimedOut(timedOut, cancel);
                break;
            case CommandUpdateAvailable updateAvailable:
                await RunCommandUpdateAvailable(updateAvailable, cancel);
                break;
            default:
                throw new InvalidOperationException($"Invalid command: {command}");
        }
    }

    private async Task RunCommandRestart(CancellationToken cancel)
    {
        if (_stopped)
        {
            _logger.LogDebug("Clearing stopped flag due to manual server restart");
            _stopped = false;
        }

        if (_runningServer == null)
        {
            _loadFailCount = 0;
            _startupFailUpdateWait = false;
            await StartServer(cancel);
            return;
        }

        await ForceShutdownServerAsync(cancel);
    }

    private async Task RunCommandStop(ServerInstanceStopCommand stopCommand, CancellationToken cancel)
    {
        // TODO: use stopCommand to indicate more extensive error message to error.

        _stopped = true;
        if (IsRunning)
        {
            _logger.LogTrace("Server is running, sending fake update notification to make it stop");
            await SendUpdateNotificationAsync(cancel);
        }
    }

    private async Task RunCommandUpdateAvailable(CommandUpdateAvailable command, CancellationToken cancel)
    {
        _updateOnRestart = command.UpdateAvailable;
        if (command.UpdateAvailable)
        {
            if (IsRunning)
            {
                _logger.LogTrace("Server is running, sending update notification.");
                await SendUpdateNotificationAsync(cancel);
            }
            else if (_stopped)
            {
                _logger.LogInformation("Not restarting server for update as it was manually stopped.");
            }
            else if (_startupFailUpdateWait)
            {
                _startupFailUpdateWait = false;
                _logger.LogInformation("Starting failed server after update.");
                await StartServer(cancel);
            }
        }
    }

    private Task RunCommandTimedOut(CommandTimedOut timedOut, CancellationToken cancel)
    {
        if (timedOut.TimeoutCounter != _serverTimeoutNumber)
        {
            _logger.LogTrace("Timeout was wrong number, ignoring");

            // Guard against race condition: the timeout could happen just before we can cancel it
            // (due to ping, server shutdown, etc).
            // We use the sequence number to avoid letting it go through in that case.
            return Task.CompletedTask;
        }

        TimeoutKill();
        return Task.CompletedTask;
    }

    private Task RunCommandServerPing(CommandServerPing ping, CancellationToken cancel)
    {
        _logger.LogTrace("Received ping from server.");
        _lastPing = DateTime.Now;

        StartTimeoutTimer();

        return Task.CompletedTask;
    }

    private async Task RunCommandServerExit(CommandServerExit exit, CancellationToken cancel)
    {
        if (_startNumber != exit.StartNumber)
        {
            // Ok so I don't know the current architecture can exactly have this happen, but here goes:
            // Suppose the server hangs. In the timeout handler, we kill it. We then immediately restart it.
            // The exit monitor would see the process exit and raise a command.
            // We obviously don't want this to start the server again,
            // so we use the start number to tell "was this exit for the last server?".
            //
            // Basically, if this if passes, this exit event is for the last server process,
            // and we already started a new one. So ignore this!

            return;
        }

        if (exit.ExitCode != 0)
            _notificationManager.SendNotification($"Server `{Key}` exited with non-success exit code: {exit.ExitCode}. Check server logs for possible causes.");

        _runningServer = null;
        if (_lastPing == null)
        {
            // If the server shuts down before sending a ping, ever, we assume it crashed during init.
            _loadFailCount += 1;
            _logger.LogWarning("{Key} shut down before sending ping on attempt {attempt}", Key,
                _loadFailCount);

            if (_loadFailCount >= LoadFailMax)
            {
                _startupFailUpdateWait = true;
                // Server keeps crashing during init, wait for an update to fix it.
                _logger.LogWarning("{Key} is failing to start, giving up until update or manual intervention.", Key);
                _notificationManager.SendNotification($"Server `{Key}` is failing to start, needs manual intervention or update.");
                return;
            }
        }
        else
        {
            _loadFailCount = 0;
        }

        if (!_stopped)
        {
            _logger.LogInformation("{Key}: Restarting server after exit...", Key);
            await StartServer(cancel);
        }
        else
        {
            _logger.LogInformation("{Key}: Not restarting server as it was manually stopped.", Key);
            _notificationManager.SendNotification($"Server `{Key}` has exited after manual stop request.");
        }
    }

    private async Task RunCommandStart(CancellationToken cancel)
    {
        await StartServer(cancel);
    }

    private async Task StartServer(CancellationToken cancel)
    {
        _logger.LogDebug("{Key}: starting server", Key);

        if (_runningServer != null)
        {
            _logger.LogTrace("Start called while already have running server process, ignoring");
            return;
        }

        if (_updateOnRestart)
        {
            _updateOnRestart = false;

            await StartRunUpdate(cancel);
        }

        GenerateNewToken();

        {
            using var con = _dataManager.OpenConnection();
            con.Execute(
                "UPDATE ServerInstance SET PersistedToken = @Secret WHERE Key = @Key",
                new
                {
                    Secret,
                    Key
                });
        }

        _lastPing = null;
        _startNumber++;

        _logger.LogTrace("Getting launch info...");

        var args = new List<string>
        {
            // Watchdog comms config.
            "--cvar", $"watchdog.token={Secret}",
            "--cvar", $"watchdog.key={Key}",
            "--cvar", $"watchdog.baseUrl={_baseServerAddress}",

            "--config-file", Path.Combine(InstanceDir, "config.toml"),
            "--data-dir", Path.Combine(InstanceDir, "data"),
        };

        // Prepare the user provided arguments
        foreach (var arg in _instanceConfig.Arguments)
        {
            args.Add(arg);
        }

        var env = new List<(string, string)>();

        foreach (var (envVar, value) in _instanceConfig.EnvironmentVariables)
        {
            env.Add((envVar, value));
        }

        // Add current build information.
        if (_currentRevision != null && _updateProvider != null)
        {
            foreach (var (cVar, value) in _updateProvider.GetLaunchCVarOverrides(_currentRevision))
            {
                args.Add("--cvar");
                args.Add($"{cVar}={value}");
            }
        }

        var startData = new ProcessStartData(
            GetProgramPath(),
            InstanceDir,
            args,
            env);

        _logger.LogTrace("Launching...");
        try
        {
            _runningServer = await _processManager.StartServer(this, startData, cancel);
            _logger.LogDebug("Launched! {Process}", _runningServer);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Exception while launching!");
        }

        MonitorServer(_startNumber, cancel);
        StartTimeoutTimer();
    }

    private string GetProgramPath()
    {
        return Path.Combine(InstanceDir, _instanceConfig.RunCommand);
    }

    private async void MonitorServer(int startNumber, CancellationToken cancel = default)
    {
        if (_runningServer == null)
            return;

        _logger.LogDebug("Starting to monitor running server");

        try
        {
            await _runningServer.WaitForExitAsync(cancel);

            _logger.LogInformation("{Key} shut down with exit code {ExitCode}", Key,
                _runningServer.ExitCode);

            await _commandQueue.Writer.WriteAsync(new CommandServerExit(startNumber, _runningServer.ExitCode), cancel);
        }
        catch (OperationCanceledException)
        {
            // Nada.
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while waiting on process exit for {Key}", Key);
        }
    }

    private async Task StartRunUpdate(CancellationToken cancel)
    {
        if (_updateProvider == null)
            return;

        bool hasUpdate;
        try
        {
            _logger.LogTrace("Checking for update...");
            hasUpdate = await _updateProvider.CheckForUpdateAsync(_currentRevision, cancel);
            _logger.LogDebug("Update available: {available}.", hasUpdate);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while checking for update!");
            return;
        }

        if (!hasUpdate)
            return;

        _logger.LogTrace("Starting update...");
        string? newRevision;
        try
        {
            newRevision = await _updateProvider.RunUpdateAsync(
                _currentRevision,
                Path.Combine(InstanceDir, "bin"),
                cancel);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Uncaught error while updating server");
            return;
        }

        if (newRevision != null)
        {
            _logger.LogDebug("Updated from {current} to {new}.",
                _currentRevision ?? "<none>",
                newRevision);

            _loadFailCount = 0;
            _currentRevision = newRevision;
            SaveData();
        }
        else
        {
            _logger.LogError("Failed to update!");
        }
    }

    /// <summary>
    /// Base class for all commands that are executed by the server instance actor.
    /// </summary>
    private abstract record Command;

    /// <summary>
    /// Mark the server as having an update available, which will be checked & downloaded the next time it restarts.
    /// </summary>
    private sealed record CommandUpdateAvailable(bool UpdateAvailable) : Command;

    /// <summary>
    /// Command to start the server, if it's not already running.
    /// </summary>
    private sealed record CommandStart : Command;

    /// <summary>
    /// Command to restart the server.
    /// </summary>
    private sealed record CommandRestart : Command;

    /// <summary>
    /// Command to stop the server gracefully, without restarting it afterwards.
    /// </summary>
    private sealed record CommandStop(ServerInstanceStopCommand StopCommand) : Command;

    /// <summary>
    /// The server has failed to ping back in time, grab the axe!
    /// </summary>
    private sealed record CommandTimedOut(int TimeoutCounter) : Command;

    /// <summary>
    /// The server has exited while being monitored.
    /// </summary>
    private sealed record CommandServerExit(int StartNumber, int ExitCode) : Command;

    /// <summary>
    /// The server has sent us a ping, it's still kicking!
    /// </summary>
    private sealed record CommandServerPing : Command;
}
