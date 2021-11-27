using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Mono.Unix;

namespace SS14.Watchdog.Components.Updates
{
    /// <summary>
    ///     Provides a mechanism for updating the server instance.
    /// </summary>
    public abstract class UpdateProvider
    {
        protected const string ClientZipName = "SS14.Client.zip";
        protected string ServerZipName => $"SS14.Server_{GetHostSS14RID()}.zip";

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
        protected static string GetHostSS14RID()
        {
            return GetHostPlatformName() + "-" + GetHostArchitectureName();
        }

        [Pure]
        private static string GetHostPlatformName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "win";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return "linux";
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return "osx";
            }

            throw new PlatformNotSupportedException();
        }

        [Pure]
        private static string GetHostArchitectureName()
        {
            switch (RuntimeInformation.OSArchitecture)
            {
                case Architecture.X86:
                    return "x86";

                case Architecture.X64:
                    return "x64";

                case Architecture.Arm:
                    return "arm";

                case Architecture.Arm64:
                    return "arm64";

                // Other architectures not supported.
                // Even some of these aren't but there's no reason not to force it to fail here.
                default:
                    throw new PlatformNotSupportedException();
            }
        }
        
        [Pure]
        protected static string GetFileHash(string filePath)
        {
            using var file = File.OpenRead(filePath);
            using var sha = SHA256.Create();

            return Convert.ToHexString(sha.ComputeHash(file));
        }

        protected static void DoBuildExtract(Stream stream, string binPath)
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            archive.ExtractToDirectory(binPath);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // chmod +x Robust.Server

                var rsPath = Path.Combine(binPath, "Robust.Server");
                if (File.Exists(rsPath))
                {
                    var f = new UnixFileInfo(rsPath);
                    f.FileAccessPermissions |=
                        FileAccessPermissions.UserExecute | FileAccessPermissions.GroupExecute |
                        FileAccessPermissions.OtherExecute;
                }
            }
        }
    }
}
