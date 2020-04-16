namespace SS14.Watchdog.Configuration
{
    public sealed class InstanceConfiguration
    {
        public string? Name { get; set; }
        public string? UpdateType { get; set; }
        public string? ApiToken { get; set; }
        public ushort ApiPort { get; set; }
        public string RunCommand { get; set; } = "bin/Robust.Server";
    }
}