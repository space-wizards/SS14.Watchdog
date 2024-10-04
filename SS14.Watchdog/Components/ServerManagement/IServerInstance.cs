using System.Threading;
using System.Threading.Tasks;

namespace SS14.Watchdog.Components.ServerManagement
{
    // Why do I even write useless comments like this one.
    // Is it because I like seeing green in my IDE?
    // Is it because I need to bump up the LOC numbers?

    /// <summary>
    ///     Manages a single game server instance.
    /// </summary>
    public interface IServerInstance
    {
        /// <summary>
        ///     Identifier of this server instance on disk, in config files, and from the watchdog API.
        /// </summary>
        string Key { get; }

        /// <summary>
        ///     Secret token used for authentication communications between the watchdog and the running game server.
        /// </summary>
        /// <remarks>
        ///     Gets regenerated on each server start.
        /// </remarks>
        string? Secret { get; }

        /// <summary>
        ///     API token used to authenticate API requests to the watchdog concerning this server instance.
        /// </summary>
        string? ApiToken { get; }

        /// <summary>
        ///     The root filesystem directory for the instance, which contains config file, data,
        ///     and probably binaries too.
        /// </summary>
        string InstanceDir { get; }

        /// <summary>
        ///     Server has sent a ping to the watchdog confirming that it is, in fact, still alive.
        /// </summary>
        Task PingReceived();

        /// <summary>
        ///     Check for update and inform game server of available update if there is one.
        /// </summary>
        void HandleUpdateCheck();

        /// <summary>
        ///     Try to tell the server to shut down gracefully.
        /// </summary>
        /// <remarks>
        /// <p>
        ///     Note that the watchdog will currently just automatically restart the server when it shuts down.
        /// </p>
        /// <p>
        ///     This method also does not do a non-graceful shutdown of the server, if it is, say, stuck.
        /// </p>
        /// </remarks>
        Task SendShutdownNotificationAsync(CancellationToken cancel = default);

        Task ForceShutdownServerAsync(CancellationToken cancel = default);
        Task DoRestartCommandAsync(CancellationToken cancel = default);

        /// <summary>
        /// Instruct that the server instance should be stopped gracefully.
        /// It will not be restarted automatically after shutdown.
        /// </summary>
        /// <remarks>
        /// The server will be asked to gracefully shut down via the <c>/update</c> end point.
        /// </remarks>
        Task DoStopCommandAsync(ServerInstanceStopCommand stopCommand, CancellationToken cancel = default);
    }

    /// <summary>
    /// Information about a stop command sent to a server instance.
    /// </summary>
    /// <seealso cref="IServerInstance.DoStopCommandAsync"/>
    public sealed class ServerInstanceStopCommand
    {
        public ServerInstanceStopReason StopReason;
    }

    /// <seealso cref="ServerInstanceStopCommand"/>
    public enum ServerInstanceStopReason
    {
        Unknown,
        Maintenance,
    }
}
