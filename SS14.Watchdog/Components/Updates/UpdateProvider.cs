using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
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
        /// <returns>Description of the update, including version number and download URLs for clients.</returns>
        public abstract Task<RevisionDescription?> RunUpdateAsync(string? currentVersion, string binPath,
            CancellationToken cancel = default);

        [Pure]
        protected static string GetBuildFilename(string platform)
        {
            return $"SS14.Client_{platform}_x64.zip";
        }
        
        [Pure]
        protected static string GetHostPlatformName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return PlatformNameWindows;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return PlatformNameLinux;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return PlatformNameMacOS;
            }

            throw new PlatformNotSupportedException();
        }

        [Pure]
        protected static string GetHostArchitectureName()
        {
            switch (RuntimeInformation.OSArchitecture)
            {
                case Architecture.X64:
                    return "x64";

                case Architecture.Arm64:
                    return "ARM64";

                // Any other architecture is unsupported.
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        [Pure]
        protected static string GetFileHash(string filePath)
        {
            using var file = File.OpenRead(filePath);
            using var sha = SHA256.Create();

            return ByteArrayToString(sha.ComputeHash(file));
        }

        [Pure]
        // https://stackoverflow.com/a/311179/4678631
        private static string ByteArrayToString(byte[] ba)
        {
            var hex = new StringBuilder(ba.Length * 2);
            foreach (var b in ba)
            {
                hex.AppendFormat("{0:x2}", b);
            }

            return hex.ToString();
        }
    }
}