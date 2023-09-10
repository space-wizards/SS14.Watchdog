using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using SS14.Watchdog.Components.ServerManagement;

namespace SS14.Watchdog.Components.ProcessManagement;

/// <summary>
/// Responsible for managing game server processes: start, stop, persistence.
/// </summary>
/// <seealso cref="IProcessHandle"/>
public interface IProcessManager
{
    bool CanPersist { get; }

    Task<IProcessHandle> StartServer(
        IServerInstance instance,
        ProcessStartData data,
        CancellationToken cancel = default);

    Task<IProcessHandle?> TryGetPersistedServer(IServerInstance instance,
        string program,
        CancellationToken cancel);
}

/// <summary>
/// All data needed to start a game server process. Warning: big.
/// </summary>
/// <param name="Program">The program to run to launch the game server. Full path.</param>
/// <param name="WorkingDirectory">The working directory of the launched process.</param>
public sealed record ProcessStartData(
    string Program,
    string WorkingDirectory,
    IReadOnlyCollection<string> Arguments,
    IReadOnlyCollection<(string, string)> EnvironmentVariables
);

/// <summary>
/// Handle to a running game server process managed by a <see cref="IProcessManager"/>.
/// </summary>
public interface IProcessHandle
{
    void DumpProcess(string file, DumpType type);

    Task WaitForExitAsync(CancellationToken cancel = default);

    Task Kill();

    Task<ProcessExitStatus?> GetExitStatusAsync();
}

/// <summary>
/// Status for how a process has exited.
/// </summary>
/// <param name="Reason">The reason why the process exited. Check the enum for possible values.</param>
/// <param name="Status">
/// Reason-specific value.
/// For <see cref="ProcessExitReason.ExitCode"/> this is the exit code.
/// For <see cref="ProcessExitReason.Signal"/> and <see cref="ProcessExitReason.CoreDumped"/> this is the signal that killed the process.
/// </param>
/// <seealso cref="IProcessHandle"/>
public sealed record ProcessExitStatus(ProcessExitReason Reason, int Status)
{
    public ProcessExitStatus(ProcessExitReason reason) : this(reason, 0)
    {
    }

    public bool IsClean => Reason == ProcessExitReason.ExitCode && Status == 0 || Reason == ProcessExitReason.Success;
}

/// <summary>
/// Reason values for <see cref="ProcessExitStatus"/>.
/// </summary>
public enum ProcessExitReason
{
    // These somewhat correspond to systemd's values for "Result" on a Service, kinda.
    // https://www.freedesktop.org/software/systemd/man/org.freedesktop.systemd1.html#Properties2

    /// <summary>
    /// Process exited "successfully" according to systemd.
    /// </summary>
    /// <remarks>
    /// This probably means exit code 0, but I want to distinguish them as technically they're not equal.
    /// </remarks>
    Success,

    /// <summary>
    /// Process exited recorded exit code.
    /// </summary>
    ExitCode,

    /// <summary>
    /// Process was killed by uncaught signal.
    /// </summary>
    /// <remarks>
    /// This won't apply if the process is killed with SIGTERM,
    /// as the game handles that and manually returns exit code signum + 128.
    /// </remarks>
    Signal,
    
    /// <summary>
    /// Process crashed and dumped core.
    /// </summary>
    CoreDump,

    /// <summary>
    /// Systemd operation failed.
    /// </summary>
    SystemdFailed,

    /// <summary>
    /// Timeout executing service operation.
    /// </summary>
    Timeout,

    /// <summary>
    /// Process was killed by the Linux OOM killer.
    /// </summary>
    OomKill,

    /// <summary>
    /// Catch-all for other unhandled status codes.
    /// </summary>
    Other,
}