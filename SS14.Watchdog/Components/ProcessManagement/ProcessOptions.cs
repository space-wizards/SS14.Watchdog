namespace SS14.Watchdog.Components.ProcessManagement;

/// <summary>
/// Options for server instance process management.
/// </summary>
public sealed class ProcessOptions
{
    public const string Position = "Process";

    /// <summary>
    /// Whether to try to persist game servers through watchdog restart.
    /// If enabled, the watchdog shutting down will not stop the game server processes,
    /// and the watchdog can take control of the processes again.
    /// </summary>
    public bool PersistServers { get; set; } = false;

    /// <summary>
    /// Controls how the watchdog manages game server processes.
    /// </summary>
    public ProcessMode Mode { get; set; } = ProcessMode.Basic;
}

/// <summary>
/// Modes for the watchdog to control game server processes.
/// </summary>
/// <seealso cref="ProcessOptions"/>
public enum ProcessMode
{
    /// <summary>
    /// Processes are managed via <see cref="ProcessManagerBasic"/>.
    /// </summary>
    Basic,
}