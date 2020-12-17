using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace SS14.Watchdog.Components.Updates
{
    /// <summary>
    ///     Provides a mechanism for updating the server instance.
    /// </summary>
    public abstract class UpdateProvider
    {
        protected const string ClientZipName = "SS14.Client.zip";

        protected const string PlatformNameWindows = "Windows";
        protected const string PlatformNameLinux = "Linux";
        protected const string PlatformNameMacOS = "macOS";

        /// <summary>
        ///     Check whether an update is available.
        /// </summary>
        /// <param name="currentVersion">The current version the server is on.</param>
        /// <param name="cancel">Cancellation token.</param>
        /// <returns>True if an update is available.</returns>
        public abstract Task<bool> CheckForUpdateAsync(string? currentVersion, CancellationToken cancel = default);

        /// <summary>
        ///     Run an update if one is available.
        /// </summary>
        /// <param name="currentVersion">The current version the server is on.</param>
        /// <param name="binPath">The bin path of the server instance, to update into.</param>
        /// <param name="cancel">Cancellation token.</param>
        /// <returns>The version updated to if an update happened successfully, null other.</returns>
        public abstract Task<string?> RunUpdateAsync(string? currentVersion, string binPath,
            CancellationToken cancel = default);

        public virtual IEnumerable<KeyValuePair<string, string>> GetLaunchCVarOverrides(string currentVersion)
        {
            return Enumerable.Empty<KeyValuePair<string, string>>();
        }

        [Pure]
        protected static string GetBuildFilename(string platform)
        {
            return $"SS14.Client_{platform}_x64.zip";
        }
    }
}