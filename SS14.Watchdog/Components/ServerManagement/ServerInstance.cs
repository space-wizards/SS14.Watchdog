using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SS14.Watchdog.Components.BackgroundTasks;
using SS14.Watchdog.Components.DataManagement;
using SS14.Watchdog.Components.ProcessManagement;
using SS14.Watchdog.Components.Updates;
using SS14.Watchdog.Configuration;
using SS14.Watchdog.Configuration.Updates;

namespace SS14.Watchdog.Components.ServerManagement
{
    public sealed partial class ServerInstance : IServerInstance
    {
        /// <summary>
        ///     How many times the server can shut down before sending a ping before we stop trying to restart it.
        /// </summary>
        /// <remarks>
        ///     The assumption is that if the server shuts down before sending a ping, it has crashed during init.
        /// </remarks>
        private const int LoadFailMax = 3;

        public string Key { get; }
        public string? Secret { get; private set; }
        public string? ApiToken => _instanceConfig.ApiToken;

        public bool IsRunning => _runningServer != null;

        /// <summary>
        ///     How long since the last ping before we consider the server "dead" and forcefully terminate it.
        /// </summary>
        private TimeSpan PingTimeoutDelay => TimeSpan.FromSeconds(_instanceConfig.TimeoutSeconds);

        private readonly HttpClient _serverHttpClient = new HttpClient();

        private InstanceConfiguration _instanceConfig;
        private readonly IConfiguration _configuration;
        private readonly UpdateProvider? _updateProvider;
        private readonly ServersConfiguration _serversConfiguration;
        private readonly ILogger<ServerInstance> _logger;
        private readonly IBackgroundTaskQueue _taskQueue;
        private readonly DataManager _dataManager;
        private readonly IProcessManager _processManager;

        private string? _currentRevision;
        private bool _updateOnRestart = true;

        // Set when the server keeps failing to start so we wait for an update to come in to fix it.
        private bool _startupFailUpdateWait;
        private int _loadFailCount;

        private IProcessHandle? _runningServer;

        public ServerInstance(
            string key,
            InstanceConfiguration instanceConfig,
            IConfiguration configuration,
            ServersConfiguration serversConfiguration,
            ILogger<ServerInstance> logger,
            IBackgroundTaskQueue taskQueue,
            IServiceProvider serviceProvider,
            DataManager dataManager,
            IProcessManager processManager)
        {
            Key = key;
            _instanceConfig = instanceConfig;
            _configuration = configuration;
            _serversConfiguration = serversConfiguration;
            _logger = logger;
            _taskQueue = taskQueue;
            _dataManager = dataManager;
            _processManager = processManager;

            if (!string.IsNullOrEmpty(_instanceConfig.ApiTokenFile))
            {
                _instanceConfig.ApiToken = File.ReadAllText(_instanceConfig.ApiTokenFile);
            }

            switch (instanceConfig.UpdateType)
            {
                case "Jenkins":
                    var jenkinsConfig = configuration
                        .GetSection($"Servers:Instances:{key}:Updates")
                        .Get<UpdateProviderJenkinsConfiguration>();

                    if (jenkinsConfig == null)
                        throw new InvalidOperationException("Invalid configuration!");

                    _updateProvider = new UpdateProviderJenkins(
                        jenkinsConfig,
                        serviceProvider.GetRequiredService<ILogger<UpdateProviderJenkins>>());
                    break;

                case "Local":
                    var localConfig = configuration
                        .GetSection($"Servers:Instances:{key}:Updates")
                        .Get<UpdateProviderLocalConfiguration>();

                    if (localConfig == null)
                        throw new InvalidOperationException("Invalid configuration!");

                    _updateProvider = new UpdateProviderLocal(
                        this,
                        localConfig,
                        serviceProvider.GetRequiredService<ILogger<UpdateProviderLocal>>(),
                        configuration);
                    break;

                case "Git":
                    var gitConfig = configuration
                        .GetSection($"Servers:Instances:{key}:Updates")
                        .Get<UpdateProviderGitConfiguration>();

                    if (gitConfig == null)
                        throw new InvalidOperationException("Invalid configuration!");

                    _updateProvider = new UpdateProviderGit(
                        this,
                        gitConfig,
                        serviceProvider.GetRequiredService<ILogger<UpdateProviderGit>>(),
                        configuration);
                    break;

                case "Manifest":
                    var manifestConfig = configuration
                        .GetSection($"Servers:Instances:{key}:Updates")
                        .Get<UpdateProviderManifestConfiguration>();

                    if (manifestConfig == null)
                        throw new InvalidOperationException("Invalid configuration!");

                    _updateProvider = new UpdateProviderManifest(
                        manifestConfig,
                        serviceProvider.GetRequiredService<ILogger<UpdateProviderManifest>>());
                    break;

                case "Dummy":
                    _updateProvider = new UpdateProviderDummy();
                    break;

                case null:
                    _updateProvider = null;
                    break;

                default:
                    throw new ArgumentException($"Unknown update type: {instanceConfig.UpdateType}");
            }

            if (!Directory.Exists(InstanceDir))
            {
                Directory.CreateDirectory(InstanceDir);
                _logger.LogInformation("Created InstanceDir {InstanceDir}", InstanceDir);
            }

            LoadData();
        }

        public void OnConfigUpdate(InstanceConfiguration cfg)
        {
            _instanceConfig = cfg;
        }

        private void LoadData()
        {
            using var con = _dataManager.OpenConnection();

            var revision = con.QuerySingle<string>(
                "SELECT Revision FROM ServerInstance WHERE Key = @Key",
                new { Key });

            // Actually copy data over.
            _currentRevision = revision;
        }

        private void SaveData()
        {
            using var con = _dataManager.OpenConnection();

            con.Execute(
                "UPDATE ServerInstance SET Revision = @Revision WHERE Key = @Key",
                new
                {
                    Revision = _currentRevision,
                    Key
                });
        }

        private void GenerateNewToken()
        {
            Span<byte> raw = stackalloc byte[64];
            RandomNumberGenerator.Fill(raw);

            var token = Convert.ToBase64String(raw);

            SetToken(token);
        }

        private void SetToken(string token)
        {
            Secret = token;
            _logger.LogTrace("Server token is {Token}", token);

            if (_logger.IsEnabled(LogLevel.Trace))
            {
                var combined = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Key}:{token}"));
                _logger.LogTrace("Authorization: Basic {BasicAuth}", combined);
            }

            _serverHttpClient.DefaultRequestHeaders.Remove("WatchdogToken");
            _serverHttpClient.DefaultRequestHeaders.Add("WatchdogToken", token);
        }

        public string InstanceDir => Path.Combine(
            Environment.CurrentDirectory,
            _serversConfiguration.InstanceRoot,
            Key);

        public async Task ShutdownAsync()
        {
            _logger.LogTrace("Shutting down server instance {Key}", Key);

            if (IsRunning)
            {
                _logger.LogInformation("Shutting down running server {Key}", Key);
                await ForceShutdownServerAsync();
            }
        }

        public void ForceShutdown()
        {
            _runningServer?.Kill();
        }

        public async Task PingReceived()
        {
            // TODO: Ok so there's a mild race condition here:
            // It's possible for the server to send a ping JUST as it gets killed.
            // This ping gets queued, and due to the ordering in the entire system,
            // this ping will then be immediately seen as the next process' ping.
            await _commandQueue.Writer.WriteAsync(new CommandServerPing());
        }

        private async void StartTimeoutTimer()
        {
            _serverTimeoutTcs?.Cancel();

            var number = ++_serverTimeoutNumber;

            _serverTimeoutTcs = new CancellationTokenSource();

            var token = _serverTimeoutTcs.Token;

            try
            {
                _logger.LogTrace("Timeout timer started");
                await Task.Delay(PingTimeoutDelay, token);

                // ReSharper disable once MethodSupportsCancellation
                await _commandQueue.Writer.WriteAsync(new CommandTimedOut(number));
            }
            catch (OperationCanceledException)
            {
                // It still lives.
                _logger.LogTrace("Timeout broken, it lives.");
            }
        }

        private void TimeoutKill()
        {
            _logger.LogWarning("{Key}: timed out, killing", Key);

            if (_runningServer == null)
                return;

            if (_instanceConfig.DumpOnTimeout)
            {
                if (!OperatingSystem.IsWindows())
                {
                    _logger.LogInformation("{Key}: making on-kill process dump of type {DumpType}",
                        Key, _instanceConfig.TimeoutDumpType);

                    try
                    {
                        var dumpDir = Path.Combine(InstanceDir, "dumps");
                        Directory.CreateDirectory(dumpDir);
                        var dumpFile = Path.Combine(dumpDir, $"dump_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}");

                        _runningServer.DumpProcess(dumpFile, _instanceConfig.TimeoutDumpType);

                        _logger.LogInformation("{Key}: Process dump written to {DumpFilePath}", Key, dumpFile);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "{Key}: exception while making process dump", Key);
                    }
                }
                else
                {
                    _logger.LogInformation("{Key}: not creating process dump: not supported on Windows", Key);
                }

                _logger.LogInformation("{Key}: killing process...", Key);
            }

            _runningServer.Kill();

            // Monitor will notice server died and pick it up.
        }

        public void HandleUpdateCheck()
        {
            if (_updateProvider == null)
                return;

            _logger.LogDebug("Received update notification.");
            _taskQueue.QueueTask(async cancel =>
            {
                var updateAvailable = await _updateProvider.CheckForUpdateAsync(_currentRevision, cancel);
                _logger.LogTrace("Update available? {available}", updateAvailable);

                await _commandQueue.Writer.WriteAsync(new CommandUpdateAvailable(updateAvailable), cancel);
            });
        }

        private async Task SendUpdateNotificationAsync(CancellationToken cancel = default)
        {
            var resp = await _serverHttpClient.PostAsync($"http://localhost:{_instanceConfig.ApiPort}/update", null!,
                cancel);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Bad HTTP status code on update notification: {status}", resp.StatusCode);
            }
        }

        public async Task DoRestartCommandAsync(CancellationToken cancel = default)
        {
            await _commandQueue.Writer.WriteAsync(new CommandRestart(), cancel);
        }

        public async Task ForceShutdownServerAsync(CancellationToken cancel = default)
        {
            var proc = _runningServer;
            if (proc == null || proc.HasExited)
            {
                return;
            }

            try
            {
                var shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                // Give it 5 seconds to respond.
                shutdownCts.CancelAfter(5000);
                await SendShutdownNotificationAsync(shutdownCts.Token);
            }
            catch (HttpRequestException e)
            {
                _logger.LogInformation(e, "Exception sending shutdown notification to server. Killing.");
                proc.Kill();
                return;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Timeout sending shutdown notification to server. Killing.");
                proc.Kill();
                return;
            }

            _logger.LogDebug("{Key} sent shutdown notification to server. Waiting for exit", Key);

            // Give it 5 seconds to shut down.
            var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            waitCts.CancelAfter(5000);
            try
            {
                await proc.WaitForExitAsync(cancel);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("{Key} did not gracefully shut down in time, killing");
                proc.Kill();
            }

            _logger.LogInformation("{Key} shut down gracefully", Key);
        }

        public async Task SendShutdownNotificationAsync(CancellationToken cancel = default)
        {
            await _serverHttpClient.PostAsync($"http://localhost:{_instanceConfig.ApiPort}/shutdown",
                new StringContent(JsonSerializer.Serialize(new ShutdownParameters
                {
                    Reason = "Watchdog shutting down."
                })), cancel);
        }

        [PublicAPI]
        private sealed class ShutdownParameters
        {
            // ReSharper disable once RedundantDefaultMemberInitializer
            public string Reason { get; set; } = default!;
        }
    }
}