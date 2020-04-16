using System.Collections.Generic;

namespace SS14.Watchdog.Configuration
{
    public sealed class ServersConfiguration
    {
        public string InstanceRoot { get; set; } = "instances/";

        public Dictionary<string, InstanceConfiguration> Instances { get; set; } =
            new Dictionary<string, InstanceConfiguration>();
    }
}