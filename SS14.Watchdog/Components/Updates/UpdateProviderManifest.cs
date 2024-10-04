using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using SS14.Watchdog.Configuration.Updates;
using SS14.Watchdog.Utility;

namespace SS14.Watchdog.Components.Updates
{
    public sealed class UpdateProviderManifest : UpdateProvider
    {
        private const int DownloadTimeoutSeconds = 120;

        private readonly HttpClient _httpClient = new();

        private readonly string _manifestUrl;
        private readonly UpdateProviderManifestConfiguration _configuration;
        private readonly ILogger<UpdateProviderManifest> _logger;

        public UpdateProviderManifest(
            UpdateProviderManifestConfiguration configuration,
            ILogger<UpdateProviderManifest> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _manifestUrl = configuration.ManifestUrl;
        }

        public override async Task<bool> CheckForUpdateAsync(
            string? currentVersion,
            CancellationToken cancel = default)
        {
            var manifest = await FetchManifestInfoAsync(cancel);
            if (manifest == null)
                return false;

            return SelectMaxVersion(manifest) != currentVersion;
        }

        public override async Task<string?> RunUpdateAsync(
            string? currentVersion,
            string binPath,
            CancellationToken cancel = default)
        {
            var manifest = await FetchManifestInfoAsync(cancel);
            if (manifest == null)
                return null;

            var maxVersion = SelectMaxVersion(manifest);
            if (maxVersion == null)
            {
                _logger.LogWarning("There are no versions, not updating");
                return null;
            }

            if (maxVersion == currentVersion)
            {
                _logger.LogDebug("Update not necessary!");
                return null;
            }

            var versionInfo = manifest.Builds[maxVersion];

            _logger.LogTrace("New version is {NewVersion} from {OldVersion}", maxVersion, currentVersion ?? "<none>");

            var rid = RidUtility.FindBestRid(versionInfo.Server.Keys);

            if (rid == null)
            {
                _logger.LogError("Unable to find compatible build for our platform!");
                return null;
            }

            var build = versionInfo.Server[rid];
            var downloadUrl = build.Url;
            var downloadHash = Convert.FromHexString(build.Sha256);

            // Create temporary file to download binary into (not doing this in memory).
            await using var tempFile = TempFile.CreateTempFile();

            _logger.LogTrace("Downloading server binary from {Download} to {TempFile}", downloadUrl, tempFile.Name);

            var timeoutTcs = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeoutTcs.CancelAfter(TimeSpan.FromSeconds(DownloadTimeoutSeconds));

            try
            {
                using var resp = await _httpClient.SendAsync(
                    MakeAuthenticatedGet(downloadUrl),
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutTcs.Token);

                _logger.LogTrace("Received HTTP response, starting download");

                resp.EnsureSuccessStatusCode();

                await resp.Content.CopyToAsync(tempFile, timeoutTcs.Token);

                _logger.LogTrace("Update file downloaded to disk");
            }
            catch (OperationCanceledException)
            {
                _logger.LogError("Timeout while downloading: {Timeout} seconds", DownloadTimeoutSeconds);
                throw;
            }

            // Download to file...

            // Verify hash because why not?
            _logger.LogTrace("Verifying hash of download file...");
            tempFile.Seek(0, SeekOrigin.Begin);
            var hashOutput = await SHA256.HashDataAsync(tempFile, cancel);

            if (!downloadHash.AsSpan().SequenceEqual(hashOutput))
            {
                _logger.LogError("Hash verification failed while updating!");
                return null;
            }

            _logger.LogTrace("Deleting old bin directory ({BinPath})", binPath);
            if (Directory.Exists(binPath))
            {
                Directory.Delete(binPath, true);
            }

            Directory.CreateDirectory(binPath);

            _logger.LogTrace("Extracting zip file");

            tempFile.Seek(0, SeekOrigin.Begin);
            DoBuildExtract(tempFile, binPath);

            return maxVersion;
        }

        private async Task<ManifestInfo?> FetchManifestInfoAsync(CancellationToken cancel)
        {
            _logger.LogDebug("Fetching build manifest from {ManifestUrl}...", _manifestUrl);
            try
            {
                using var resp = await _httpClient.SendAsync(MakeAuthenticatedGet(_manifestUrl), cancel);
                resp.EnsureSuccessStatusCode();

                var manifest = await resp.Content.ReadFromJsonAsync<ManifestInfo>(cancel);
                if (manifest == null)
                    _logger.LogError("Failed to fetch build manifest: JSON response was null!");

                return manifest;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to fetch build manifest!");
                return null;
            }
        }

        private HttpRequestMessage MakeAuthenticatedGet(string url)
        {
            var message = new HttpRequestMessage(HttpMethod.Get, url);
            if (_configuration.Authentication is { Username: { } user, Password: { } pass })
                message.Headers.Authorization = new AuthenticationHeaderValue("Basic", MakeBasicAuthValue(user, pass));

            return message;
        }

        private static string MakeBasicAuthValue(string user, string pass)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{pass}"));
        }

        private static string? SelectMaxVersion(ManifestInfo manifest)
        {
            if (manifest.Builds.Count == 0)
                return null;

            return manifest.Builds.Aggregate((a, b) => a.Value.Time > b.Value.Time ? a : b).Key;
        }

        private sealed record ManifestInfo
        {
            [UsedImplicitly]
            public ManifestInfo(Dictionary<string, VersionInfo> builds)
            {
                Builds = builds;
            }

            [JsonPropertyName("builds")] public Dictionary<string, VersionInfo> Builds { get; }
        }

        private sealed record VersionInfo
        {
            [JsonPropertyName("time")] public DateTimeOffset Time { get; }
            [JsonPropertyName("client")] public DownloadInfo Client { get; }
            [JsonPropertyName("server")] public Dictionary<string, DownloadInfo> Server { get; }

            [UsedImplicitly]
            public VersionInfo(DateTimeOffset time, DownloadInfo client, Dictionary<string, DownloadInfo> server)
            {
                Client = client;
                Server = server;
                Time = time;
            }
        }

        private sealed record DownloadInfo
        {
            [JsonPropertyName("url")] public string Url { get; }
            [JsonPropertyName("sha256")] public string Sha256 { get; }

            [UsedImplicitly]
            public DownloadInfo(string url, string sha256)
            {
                Url = url;
                Sha256 = sha256;
            }
        }
    }
}
