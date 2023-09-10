using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SS14.Watchdog.Components.DataManagement;
using SS14.Watchdog.Components.ServerManagement;
using systemd1.DBus;
using Tmds.DBus;

namespace SS14.Watchdog.Components.ProcessManagement;

/// <summary>
/// asdf
/// </summary>
/// <seealso cref="SystemdProcessOptions"/>
public sealed class ProcessManagerSystemd : IProcessManager, IHostedService
{
    private const string ServiceSystemd = "org.freedesktop.systemd1";

    private readonly ILogger<ProcessManagerSystemd> _logger;
    private readonly SystemdProcessOptions _options;
    private readonly DataManager _dataManager;

    private Connection? _dbusConnection = null!;
    private IManager? _systemd = null!;

    public bool CanPersist => _options.PersistServers;

    public ProcessManagerSystemd(ILogger<ProcessManagerSystemd> logger, DataManager dataManager, IOptions<SystemdProcessOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _dataManager = dataManager;
    }

    public async Task<IProcessHandle> StartServer(IServerInstance instance, ProcessStartData data, CancellationToken cancel = default)
    {
        Debug.Assert(_systemd != null);
        Debug.Assert(_dbusConnection != null);

        _logger.LogDebug("Starting game server process for instance {Key}: {Program}", instance.Key, data.Program);

        string unitName;
        if (_options.UnitManagementMode == SystemdUnitManagementMode.TransientFixed)
        {
            unitName = GetUnitName(instance);
            _logger.LogTrace("Unit name is {UnitName}. Making sure it doesn't exist...", unitName);

            await MakeSureExistingUnitGone(unitName, cancel);
        }
        else
        {
            unitName = GetUnitNameRandom(instance);
            _logger.LogTrace("Unit name is {UnitName}", unitName);
        }

        var properties = new List<(string, object)>();

        _logger.LogTrace("Working directory: {WorkingDir}", data.WorkingDirectory);

        var args = new List<string>();
        args.Add(Path.GetFileName(data.Program));
        foreach (var argument in data.Arguments)
        {
            _logger.LogTrace("Arg: {Argument}", argument);
            args.Add(argument);
        }

        var env = new List<string>();
        foreach (var (envVar, envValue) in data.EnvironmentVariables)
        {
            _logger.LogTrace("Env: {EnvVar} = {EnvValue}", envVar, envValue);
            env.Add($"{envVar}={envValue}");
        }

        properties.Add(("ExecStart", new ExecCommand[]{ new ExecCommand(data.Program, args.ToArray(), false) }));
        properties.Add(("WorkingDirectory", data.WorkingDirectory));
        properties.Add(("Environment", env.ToArray()));
        properties.Add(("SuccessExitStatus", new ExitStatusSet(new int[] {143}, Array.Empty<int>())));
        // Set KillSignal to SIGKILL: we use the HTTP API to request graceful termination,
        // If we tell systemd to stop the game server, it's gotta be forceful.
        properties.Add(("KillSignal", 9));

        _logger.LogTrace($"Running StartTransientUnit...");

        // await _systemd.SetUnitPropertiesAsync(unitName, false, properties.ToArray());

        await _systemd.StartTransientUnitAsync(
            unitName,
            "replace",
            properties.ToArray(),
            Array.Empty<(string, (string, object)[])>());

        var unit = await _systemd.GetUnitAsync(unitName);

        if (CanPersist)
        {
            PersistUnitName(instance, unitName);
        }

        return new Handle(this, unit);
    }

    private void PersistUnitName(IServerInstance instance, string unitName)
    {
        _logger.LogDebug("Persisting unit name to database...");

        using var con = _dataManager.OpenConnection();
        using var tx = con.BeginTransaction();

        con.Execute(
            "UPDATE ServerInstance SET PersistedSystemdUnit = @Unit WHERE Key = @Key", new
            {
                Unit = unitName,
                instance.Key
            });

        tx.Commit();
    }

    private async Task MakeSureExistingUnitGone(string unitName, CancellationToken cancel)
    {
        Debug.Assert(_systemd != null);
        Debug.Assert(_dbusConnection != null);

        ObjectPath unitPath;
        try
        {
            unitPath = await _systemd.GetUnitAsync(unitName);

            _logger.LogDebug("Unit {UnitName} already existed. Making sure it's gone", unitName);

            var unit = _dbusConnection.CreateProxy<IUnit>(ServiceSystemd, unitPath);
            var unitState = await unit.GetActiveStateAsync();

            _logger.LogTrace("Unit {UnitName} state is {State}", unitName, unitState);

            if (unitState != "failed")
            {
                _logger.LogTrace("Killing unit {UnitName} to see where that gets us", unitName);

                var job = await unit.StopAsync("replace");
                await WaitForSystemdJob(job, cancel);

                unitState = await unit.GetActiveStateAsync();

                _logger.LogTrace("New state of {UnitName} is {State}", unitName, unitState);
            }

            if (unitState == "failed")
            {
                _logger.LogTrace("Running reset-failed on {UnitName}", unitName);

                await unit.ResetFailedAsync();
            }

            // TODO: This sucks.
            await Task.Delay(1000, cancel);

            _logger.LogTrace("Alright, unit {UnitName} should be gone now, right?", unitName);
            // This will trigger the NoSuchUnit error.
            await _systemd.GetUnitAsync(unitName);
        }
        catch (DBusException e) when (e.ErrorName == "org.freedesktop.systemd1.NoSuchUnit")
        {
            _logger.LogTrace("Unit {UnitName} never existed or stopped existing while we were killing it, good!", unitName);
            return;
        }
    }

    private async Task WaitForSystemdJob(ObjectPath jobPath, CancellationToken cancel)
    {
        Debug.Assert(_dbusConnection != null);

        _logger.LogTrace("Waiting on job {JobPath} to finish...", jobPath);
        while (true)
        {
            // TODO: Use systemd's JobRemoved signal to wait for this more gracefully.
            await Task.Delay(500, cancel);
            try
            {
                await _dbusConnection.CreateProxy<IJob>(ServiceSystemd, jobPath).GetAsync<uint>("Id");
            }
            catch (DBusException e) when (e.ErrorName == "org.freedesktop.DBus.Error.UnknownObject")
            {
                return;
            }
        }
    }

    public async Task<IProcessHandle?> TryGetPersistedServer(IServerInstance instance, string program, CancellationToken cancel)
    {
        try
        {
            using var con = _dataManager.OpenConnection();
            var unitName = con.QuerySingle<string?>(
                "SELECT PersistedSystemdUnit FROM ServerInstance WHERE Key = @Key",
                new
                {
                    instance.Key
                });

            _logger.LogTrace("Persisted unit name: {Pid}", unitName);
            if (unitName == null)
                return null;

            var unitPath = await _systemd!.GetUnitAsync(unitName);
            var unit = _dbusConnection!.CreateProxy<IUnit>(ServiceSystemd, unitPath);
            var state = await unit.GetActiveStateAsync();
            if (state != "active")
            {
                _logger.LogDebug("Unit exists but has bad state: {Status}", state);
                return null;
            }

            return new Handle(this, unitPath);
        }
        catch (DBusException e) when (e.ErrorName == "org.freedesktop.DBus.Error.UnknownObject" || e.ErrorName == "org.freedesktop.systemd1.NoSuchUnit")
        {
            _logger.LogTrace(e, "Error while trying to find persisted server. This probably just indicates the server is gone.");
            return null;
        }
    }

    private string GetUnitName(IServerInstance instance)
    {
        return $"{_options.UnitPrefix}{instance.Key}.service";
    }

    private string GetUnitNameRandom(IServerInstance instance)
    {
        var random = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLower();
        return $"{_options.UnitPrefix}{instance.Key}-{random}.service";
    }

    Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        _dbusConnection = _options.Manager == SystemdManager.Session ? Connection.Session : Connection.System;

        _systemd = _dbusConnection.CreateProxy<IManager>(ServiceSystemd, "/org/freedesktop/systemd1");
        //await _systemd.SubscribeAsync();

        return Task.CompletedTask;
    }

    Task IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private sealed class Handle : IProcessHandle
    {
        private readonly ProcessManagerSystemd _parent;
        private readonly ObjectPath _unitPath;

        private ProcessExitStatus? _cachedExitStatus;

        public Handle(ProcessManagerSystemd parent, ObjectPath unitPath)
        {
            _parent = parent;
            _unitPath = unitPath;
        }

        public void DumpProcess(string file, DumpType type)
        {
            // throw new System.NotImplementedException();
        }

        public async Task Kill()
        {
            try
            {
                var service = _parent._dbusConnection!.CreateProxy<IUnit>(ServiceSystemd, _unitPath);
                await service.StopAsync("replace");
            }
            catch (DBusException e) when (e.ErrorName == "org.freedesktop.DBus.Error.UnknownObject" || e.ErrorName == "org.freedesktop.systemd1.NoSuchUnit")
            {
                return;
            }
        }

        public async Task WaitForExitAsync(CancellationToken cancel = default)
        {
            // TODO: Subscribe to systemd events instead of polling.
            while (true)
            {
                await Task.Delay(1000, cancel);

                try
                {
                    var service = _parent._dbusConnection!.CreateProxy<IUnit>(ServiceSystemd, _unitPath);
                    var state = await service.GetActiveStateAsync();
                    if (state == "active" || state == "activating" || state == "deactivating")
                        continue;

                    if (state == "failed" || state == "inactive")
                        return;

                    throw new Exception($"Unexpected state for unit: {state}");
                }
                catch (DBusException e) when (e.ErrorName == "org.freedesktop.DBus.Error.UnknownObject")
                {
                    // Unit disappered, happens when it exits gracefully.
                    return;
                }
            }
        }

        public async Task<ProcessExitStatus?> GetExitStatusAsync()
        {
            return _cachedExitStatus ??= await GetExitStatusCoreAsync();
        }

        private async Task<ProcessExitStatus?> GetExitStatusCoreAsync()
        {
            try
            {
                var unit = _parent._dbusConnection!.CreateProxy<IUnit>(ServiceSystemd, _unitPath);
                var service = _parent._dbusConnection!.CreateProxy<IService>(ServiceSystemd, _unitPath);
                var state = await unit.GetActiveStateAsync();
                if (state == "active" || state == "activating" || state == "deactivating")
                    return null;

                _parent._logger.LogDebug("Service has state: {State}", state);

                if (state == "failed")
                {
                    var result = await service.GetResultAsync();
                    var status = await service.GetExecMainStatusAsync();

                    _parent._logger.LogDebug("Service is failed. Result: {Result}, Status: {Status}", result, status);

                    var reason = result switch
                    {
                        "timeout" => ProcessExitReason.Timeout,
                        "exit-code" => ProcessExitReason.ExitCode,
                        "signal" => ProcessExitReason.Signal,
                        "core-dump" => ProcessExitReason.CoreDump,
                        "resources" => ProcessExitReason.SystemdFailed,
                        _ => ProcessExitReason.Other
                    };

                    return new ProcessExitStatus(reason, status);
                }

                // Because this is a transient unit, it SHOULDN'T go into state "inactive".
                // It should just disappear.
                // Still though.
                if (state == "inactive")
                {
                    // So because we theoretically have the service still, we could get detailed exit status from it.
                    // Since I doubt this will ever be hit, however, I'll not bother.
                    return new ProcessExitStatus(ProcessExitReason.Success);
                }

                throw new Exception($"Unexpected state for unit: {state}");
            }
            catch (DBusException e) when (e.ErrorName == "org.freedesktop.DBus.Error.UnknownObject")
            {
                _parent._logger.LogTrace("Unit disappeared, that's success");

                // Unit disappered, happens when it exits gracefully.
                return new ProcessExitStatus(ProcessExitReason.Success);
            }
        }
    }

    public record struct ExecCommand(string Binary, string[] Arguments, bool IgnoreFailure);
    public record struct ExitStatusSet(int[] ExitCodes, int[] Signals);
}
