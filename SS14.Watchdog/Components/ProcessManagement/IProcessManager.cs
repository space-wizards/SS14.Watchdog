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
    bool HasExited { get; }
    int ExitCode { get; }

    void DumpProcess(string file, DumpType type);

    Task WaitForExitAsync(CancellationToken cancel = default);

    void Kill();
}