using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SS14.Watchdog.Components.DataManagement;
using SS14.Watchdog.Components.ServerManagement;

namespace SS14.Watchdog.Components.ProcessManagement;

/// <summary>
/// Manages processes through bog-standard process instances.
/// </summary>
/// <remarks>
/// Persistence of server processes is implemented by storing their PID.
/// </remarks>
public sealed class ProcessManagerBasic : IProcessManager
{
    private static readonly object EnvironmentLock = new();

    private readonly IOptions<ProcessOptions> _options;
    private readonly ILogger<ProcessManagerBasic> _logger;
    private readonly DataManager _dataManager;

    public bool CanPersist { get; }

    public ProcessManagerBasic(
        IOptions<ProcessOptions> options,
        ILogger<ProcessManagerBasic> logger,
        DataManager dataManager)
    {
        _options = options;
        _logger = logger;
        _dataManager = dataManager;

        CanPersist = options.Value.PersistServers;
    }

    public Task<IProcessHandle> StartServer(IServerInstance instance, ProcessStartData data,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting game server process for instance {Key}: {Program}", instance.Key, data.Program);
        _logger.LogTrace("Working directory: {WorkingDir}", data.WorkingDirectory);

        var logDirectory = EnsureLogDirectory(instance);
        var robustLogFile = Path.Combine(logDirectory, "server.log");

        if (_options.Value.LaunchInNewWindow)
        {
            _logger.LogInformation(
                "Server {Key} will launch in a visible window. Server log file: {ServerLog}",
                instance.Key,
                robustLogFile);
        }
        else
        {
            _logger.LogInformation(
                "Server {Key} process logs: stdout={StdoutLog}, stderr={StderrLog}, serverLog={ServerLog}",
                instance.Key,
                Path.Combine(logDirectory, "server.out.log"),
                Path.Combine(logDirectory, "server.err.log"),
                robustLogFile);
        }

        var startInfo = new ProcessStartInfo();
        startInfo.FileName = data.Program;
        startInfo.WorkingDirectory = data.WorkingDirectory;
        startInfo.UseShellExecute = _options.Value.LaunchInNewWindow;
        startInfo.CreateNoWindow = false;
        startInfo.RedirectStandardOutput = !_options.Value.LaunchInNewWindow;
        startInfo.RedirectStandardError = !_options.Value.LaunchInNewWindow;

        foreach (var argument in data.Arguments)
        {
            _logger.LogTrace("Arg: {Argument}", argument);
            startInfo.ArgumentList.Add(argument);
        }

        if (!startInfo.UseShellExecute)
        {
            foreach (var (var, value) in data.EnvironmentVariables)
            {
                _logger.LogTrace("Env: {EnvVar} = {EnvValue}", var, value);
                startInfo.EnvironmentVariables[var] = value;
            }

            startInfo.EnvironmentVariables["ROBUST_LOG_FILE"] = robustLogFile;
        }

        _logger.LogDebug("Launching...");

        StreamWriter? stdoutLog = null;
        StreamWriter? stderrLog = null;

        Process? process;
        try
        {
            if (_options.Value.LaunchInNewWindow)
            {
                process = StartServerInNewWindow(startInfo, data, robustLogFile);
            }
            else
            {
                stdoutLog = OpenProcessLog(logDirectory, "server.out.log");
                stderrLog = OpenProcessLog(logDirectory, "server.err.log");
                var stdoutWriter = stdoutLog;
                var stderrWriter = stderrLog;

                process = Process.Start(startInfo);

                if (process == null)
                    throw new Exception("No process was started??");

                process.OutputDataReceived += (_, e) => WriteProcessOutput(stdoutWriter, e.Data);
                process.ErrorDataReceived += (_, e) => WriteProcessOutput(stderrWriter, e.Data);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }

            if (process == null)
                throw new Exception("No process was started??");
        }
        catch (Exception e)
        {
            stdoutLog?.Dispose();
            stderrLog?.Dispose();
            _logger.LogError(e, "Process launch failed!");
            throw;
        }

        _logger.LogDebug("Started PID: {Pid}", process.Id);

        if (CanPersist)
        {
            // Save PID in the SQLite database.
            PersistPid(instance, process);
        }

        return Task.FromResult<IProcessHandle>(new Handle(process, stdoutLog, stderrLog));
    }

    private static Process? StartServerInNewWindow(
        ProcessStartInfo startInfo,
        ProcessStartData data,
        string robustLogFile)
    {
        lock (EnvironmentLock)
        {
            var previousValues = new (string Name, string? Value)[data.EnvironmentVariables.Count + 1];
            var i = 0;
            try
            {
                foreach (var (name, value) in data.EnvironmentVariables)
                {
                    previousValues[i++] = (name, Environment.GetEnvironmentVariable(name));
                    Environment.SetEnvironmentVariable(name, value);
                }

                previousValues[i++] = ("ROBUST_LOG_FILE", Environment.GetEnvironmentVariable("ROBUST_LOG_FILE"));
                Environment.SetEnvironmentVariable("ROBUST_LOG_FILE", robustLogFile);

                return Process.Start(startInfo);
            }
            finally
            {
                for (var j = 0; j < i; j++)
                    Environment.SetEnvironmentVariable(previousValues[j].Name, previousValues[j].Value);
            }
        }
    }

    private static string EnsureLogDirectory(IServerInstance instance)
    {
        var logDirectory = Path.Combine(instance.InstanceDir, "logs");
        Directory.CreateDirectory(logDirectory);
        return logDirectory;
    }

    private static StreamWriter OpenProcessLog(string logDirectory, string fileName)
    {
        return new StreamWriter(
            new FileStream(Path.Combine(logDirectory, fileName), FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
    }

    private static void WriteProcessOutput(TextWriter writer, string? message)
    {
        if (message == null)
            return;

        lock (writer)
        {
            try
            {
                writer.WriteLine(message);
            }
            catch (ObjectDisposedException)
            {
                // Process output events can arrive while the handle is being cleaned up.
            }
        }
    }

    private void PersistPid(IServerInstance instance, Process process)
    {
        _logger.LogDebug("Persisting PID to database...");

        using var con = _dataManager.OpenConnection();
        using var tx = con.BeginTransaction();

        con.Execute(
            "UPDATE ServerInstance SET PersistedPid = @Pid WHERE Key = @Key", new
            {
                Pid = process.Id,
                instance.Key
            });

        tx.Commit();
    }

    public Task<IProcessHandle?> TryGetPersistedServer(
        IServerInstance instance,
        string program,
        CancellationToken cancel)
    {
        if (!CanPersist)
            throw new InvalidOperationException("Persistence is not enabled!");

        _logger.LogTrace("Trying to load persisted server by PID...");

        // Get persisted PID from database.

        using var con = _dataManager.OpenConnection();
        var pid = con.QuerySingle<int?>(
            "SELECT PersistedPid FROM ServerInstance WHERE Key = @Key",
            new
            {
                instance.Key
            });

        _logger.LogTrace("Persisted PID: {Pid}", pid);

        if (pid == null)
            return Task.FromResult<IProcessHandle?>(null);

        Process process;
        try
        {
            process = Process.GetProcessById(pid.Value);
        }
        catch (ArgumentException)
        {
            _logger.LogDebug("Failed to locate persisted process PID");
            return Task.FromResult<IProcessHandle?>(null);
        }

        _logger.LogTrace("Located possible server process. Verifying program name.");

        if (process.MainModule == null)
        {
            _logger.LogDebug("Unable to determine main module of detected process, not trying it.");
            return Task.FromResult<IProcessHandle?>(null);
        }

        var programFullPath = Path.GetFullPath(program);
        if (process.MainModule.FileName != programFullPath)
        {
            _logger.LogDebug(
                "Matching PID has mismatching program: {DetectedProgram} (expected {ExpectedProgram})",
                process.MainModule.FileName,
                programFullPath);

            return Task.FromResult<IProcessHandle?>(null);
        }

        _logger.LogDebug("Process looks good, guess we're using this!");

        return Task.FromResult<IProcessHandle?>(new Handle(process) { IsRecovered = true });
    }

    private sealed class Handle : IProcessHandle
    {
        private readonly Process _process;
        private readonly TextWriter? _stdoutLog;
        private readonly TextWriter? _stderrLog;
        public bool IsRecovered;

        public Handle(Process process, TextWriter? stdoutLog = null, TextWriter? stderrLog = null)
        {
            _process = process;
            _stdoutLog = stdoutLog;
            _stderrLog = stderrLog;
        }

        public void DumpProcess(string file, DumpType type)
        {
            var client = new DiagnosticsClient(_process.Id);
            client.WriteDump(type, file);
        }

        public async Task WaitForExitAsync(CancellationToken cancel = default)
        {
            await _process.WaitForExitAsync(cancel);
            _process.WaitForExit();
            await DisposeLogsAsync();
        }

        public Task<ProcessExitStatus?> GetExitStatusAsync()
        {
            if (!_process.HasExited)
                return Task.FromResult<ProcessExitStatus?>(null);

            // POSIX makes it impossible to fetch the exit code for processes that aren't our immediate children.
            // This means we cannot tell what the exit code is if the process
            // was started by a previous watchdog instance, and we "recovered" it from persistence.
            // Windows does not have this issue. Microsoft wins again.
            var processExitStatus = !OperatingSystem.IsWindows() && IsRecovered
                ? new ProcessExitStatus(ProcessExitReason.ReasonUnavailable)
                : new ProcessExitStatus(ProcessExitReason.ExitCode, _process.ExitCode);

            return Task.FromResult<ProcessExitStatus?>(processExitStatus);
        }

        public Task Kill()
        {
            _process.Kill(entireProcessTree: true);

            return Task.CompletedTask;
        }

        private async Task DisposeLogsAsync()
        {
            if (_stdoutLog is IAsyncDisposable asyncStdout)
                await asyncStdout.DisposeAsync();
            else
                _stdoutLog?.Dispose();

            if (_stderrLog is IAsyncDisposable asyncStderr)
                await asyncStderr.DisposeAsync();
            else
                _stderrLog?.Dispose();
        }
    }
}
