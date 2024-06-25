using System.Collections.Generic;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Microsoft.Diagnostics.NETCore.Client;

namespace SS14.Watchdog.Configuration
{
    [UsedImplicitly]
    public sealed class InstanceConfiguration
    {
        public string? Name { get; set; }
        public string? UpdateType { get; set; }
        /// <summary>
        ///     API Token will be read from this file if set.
        ///     Takes priority over <see cref="ApiToken"/> if not null.
        /// </summary>
        public string? ApiTokenFile { get; set; }
        public string? ApiToken { get; set; }
        public ushort ApiPort { get; set; }

        public string RunCommand { get; set; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "bin/Robust.Server.exe"
            : "bin/Robust.Server";

        /// <summary>
        ///     User arguments to pass to the server process.
        /// </summary>
        public List<string> Arguments { get; set; } = [];

        /// <summary>
        /// Make a heap dump if the server is killed due to timeout. Only supported on Linux.
        /// </summary>
        public bool DumpOnTimeout { get; set; } = true;

        /// <summary>
        /// When <see cref="DumpOnTimeout"/> is on, the type of dump to make.
        /// </summary>
        public DumpType TimeoutDumpType { get; set; } = DumpType.Normal;

        /// <summary>
        /// How long since the last ping before we consider the server "dead" and forcefully terminate it. In seconds.
        /// </summary>
        public int TimeoutSeconds { get; set; } = 90;

        /// <summary>
        /// Any additional environment variables for the server process.
        /// </summary>
        [UsedImplicitly]
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new();
    }
}
