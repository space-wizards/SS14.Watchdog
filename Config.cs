using System;
using System.IO;
using System.Threading.Tasks;

namespace SS14.Watchdog
{
    public sealed class Config
    {
        public string ApiBind { get; private set; } = "http://127.0.0.1:42092/";
        public string ToRun { get; private set; }
        public string Password { get; private set; }
        public string ServerBinPath { get; private set; }
        public Uri BinaryDownloadPath { get; private set; }

        public static Config Load()
        {
            var config = new Config();
            if (!File.Exists("watchdog_config.toml"))
            {
                return config;
            }

            var file = Nett.Toml.ReadFile("watchdog_config.toml");
            if (file.TryGetValue("torun", out var value))
            {
                config.ToRun = value.Get<string>();
            }

            if (file.TryGetValue("apibind", out value))
            {
                config.ApiBind = value.Get<string>();
            }

            if (file.TryGetValue("password", out value))
            {
                config.Password = value.Get<string>();
            }

            if (file.TryGetValue("serverbinpath", out value))
            {
                config.ServerBinPath = value.Get<string>();
            }

            if (file.TryGetValue("binarydownloadpath", out value))
            {
                config.BinaryDownloadPath = new Uri(value.Get<string>());
            }

            return config;
        }

        private Config()
        {
        }
    }
}