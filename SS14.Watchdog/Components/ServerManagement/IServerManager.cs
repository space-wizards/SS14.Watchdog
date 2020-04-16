using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SS14.Watchdog.Components.ServerManagement
{
    /// <summary>
    ///     Manages <see cref="IServerInstance"/>s, starts them, and so on.
    /// </summary>
    public interface IServerManager
    {
        /// <summary>
        ///     Get an instance by the instance's <see cref="IServerInstance.Key"/>.
        /// </summary>
        /// <remarks>Standard try-get method I'm not writing the rest of this comment out.</remarks>
        public bool TryGetInstance(string key, [NotNullWhen(true)] out IServerInstance? instance);

        /// <summary>
        ///     Contains all server instances we manage.
        /// </summary>
        IReadOnlyCollection<IServerInstance> Instances { get; }
    }
}