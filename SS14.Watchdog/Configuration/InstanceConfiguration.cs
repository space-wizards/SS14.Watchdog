using System.Runtime.InteropServices;

namespace SS14.Watchdog.Configuration
{
    public sealed class InstanceConfiguration
    {
        public string? Name { get; set; }
        public string? UpdateType { get; set; }
        public string? ApiToken { get; set; }
        public ushort ApiPort { get; set; }

        public string RunCommand { get; set; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "bin/Robust.Server.exe"
            : "bin/Robust.Server";
    }
}