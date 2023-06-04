using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SS14.Watchdog.Components.ServerManagement;
using SS14.Watchdog.Configuration.Updates;

namespace SS14.Watchdog.Components.Updates
{
    /// <summary>
    ///     Update provider that allows doing manual updates on local files as updating method.
    /// </summary>
    public sealed class UpdateProviderLocal : UpdateProvider
    {
        private readonly IServerInstance _serverInstance;
        private readonly UpdateProviderLocalConfiguration _specificConfiguration;
        private readonly ILogger<UpdateProviderLocal> _logger;
        private readonly IConfiguration _configuration;

        public UpdateProviderLocal(IServerInstance serverInstance,
            UpdateProviderLocalConfiguration specificConfiguration,
            ILogger<UpdateProviderLocal> logger,
            IConfiguration configuration)
        {
            _serverInstance = serverInstance;
            _specificConfiguration = specificConfiguration;
            _logger = logger;
            _configuration = configuration;
        }

        public override Task<bool> CheckForUpdateAsync(string? currentVersion, CancellationToken cancel = default)
        {
            return Task.FromResult(currentVersion != _specificConfiguration.CurrentVersion);
        }

        public override Task<string?> RunUpdateAsync(string? currentVersion, string binPath,
            CancellationToken cancel = default)
        {
            if (currentVersion == _specificConfiguration.CurrentVersion)
            {
                return Task.FromResult<string?>(null);
            }

            // ReSharper disable once RedundantTypeArgumentsOfMethod
            return Task.FromResult<string?>(_specificConfiguration.CurrentVersion);
        }

        public override IEnumerable<KeyValuePair<string, string>> GetLaunchCVarOverrides(string currentVersion)
        {
            var binariesPath = Path.Combine(_serverInstance.InstanceDir, "binaries");
            if (!Directory.Exists(binariesPath))
            {
                throw new InvalidOperationException(
                    "Expected binaries/ directory containing all client binaries in the instance folder.");
            }

            var binariesRoot = new Uri(
                new Uri(_configuration["BaseUrl"]!),
                $"instances/{_serverInstance.Key}/binaries/");

            yield return new KeyValuePair<string, string>(
                "build.download_url",
                new Uri(binariesRoot, ClientZipName).ToString());

            var hash = GetFileHash(Path.Combine(binariesPath, ClientZipName));

            yield return new KeyValuePair<string, string>("build.hash", hash);
        }
    }
}