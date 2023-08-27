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

        var startInfo = new ProcessStartInfo();
        startInfo.FileName = data.Program;
        startInfo.WorkingDirectory = data.WorkingDirectory;
        startInfo.UseShellExecute = false;

        foreach (var argument in data.Arguments)
        {
            _logger.LogTrace("Arg: {Argument}", argument);
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var (var, value) in data.EnvironmentVariables)
        {
            _logger.LogTrace("Env: {EnvVar} = {EnvValue}", var, value);
            startInfo.EnvironmentVariables[var] = value;
        }

        _logger.LogDebug("Launching...");
        Process? process;
        try
        {
            process = Process.Start(startInfo);

            if (process == null)
                throw new Exception("No process was started??");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Process launch failed!");
            throw;
        }

        _logger.LogDebug("Started PID: {Pid}", process.Id);

        if (CanPersist)
        {
            // Save PID in the SQLite database.
            PersistPid(instance, process);
        }

        return Task.FromResult<IProcessHandle>(new Handle(process));
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

        return Task.FromResult<IProcessHandle?>(new Handle(process));
    }

    private sealed class Handle : IProcessHandle
    {
        private readonly Process _process;

        public bool HasExited => _process.HasExited;
        public int ExitCode => _process.ExitCode;

        public Handle(Process process)
        {
            _process = process;
        }

        public void DumpProcess(string file, DumpType type)
        {
            var client = new DiagnosticsClient(_process.Id);
            client.WriteDump(type, file);
        }

        public async Task WaitForExitAsync(CancellationToken cancel = default)
        {
            await _process.WaitForExitAsync(cancel);
        }

        public void Kill()
        {
            _process.Kill(entireProcessTree: true);
        }
    }
}