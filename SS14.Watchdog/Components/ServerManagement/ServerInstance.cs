using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SS14.Watchdog.Components.BackgroundTasks;
using SS14.Watchdog.Components.Updates;
using SS14.Watchdog.Configuration;
using SS14.Watchdog.Configuration.Updates;

namespace SS14.Watchdog.Components.ServerManagement
{
    public sealed class ServerInstance : IServerInstance
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

        public bool IsRunning => _runningServerProcess != null;

        /// <summary>
        ///     How long since the last ping before we consider the server "dead" and forcefully terminate it.
        /// </summary>
        private TimeSpan PingTimeoutDelay => TimeSpan.FromSeconds(_instanceConfig.TimeoutSeconds);
        
        private readonly SemaphoreSlim _stateLock = new SemaphoreSlim(1, 1);

        private readonly HttpClient _serverHttpClient = new HttpClient();

        private InstanceConfiguration _instanceConfig;
        private readonly IConfiguration _configuration;
        private readonly UpdateProvider? _updateProvider;
        private readonly ServersConfiguration _serversConfiguration;
        private readonly ILogger<ServerInstance> _logger;
        private readonly IBackgroundTaskQueue _taskQueue;

        private string? _currentRevision;
        private bool _updateOnRestart = true;
        private bool _shuttingDown;

        // Set when the server keeps failing to start so we wait for an update to come in to fix it.
        private bool _startupFailUpdateWait;
        private int _loadFailCount;

        private Process? _runningServerProcess;
        private Task? _monitorTask;

        private DateTime? _lastPing;
        private CancellationTokenSource? _serverTimeoutTcs;

        public ServerInstance(
            string key,
            InstanceConfiguration instanceConfig,
            IConfiguration configuration, 
            ServersConfiguration serversConfiguration,
            ILogger<ServerInstance> logger,
            IBackgroundTaskQueue taskQueue,
            IServiceProvider serviceProvider)
        {
            Key = key;
            _instanceConfig = instanceConfig;
            _configuration = configuration;
            _serversConfiguration = serversConfiguration;
            _logger = logger;
            _taskQueue = taskQueue;

            switch (instanceConfig.UpdateType)
            {
                case "Jenkins":
                    var jenkinsConfig = configuration
                        .GetSection($"Servers:Instances:{key}:Updates")
                        .Get<UpdateProviderJenkinsConfiguration>();

                    _updateProvider = new UpdateProviderJenkins(
                        jenkinsConfig, 
                        serviceProvider.GetRequiredService<ILogger<UpdateProviderJenkins>>());
                    break;

                case "Local":
                    var localConfig = configuration
                        .GetSection($"Servers:Instances:{key}:Updates")
                        .Get<UpdateProviderLocalConfiguration>();

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
            var dataPath = Path.Combine(InstanceDir, "data.json");

            string fileData;
            try
            {
                fileData = File.ReadAllText(dataPath);
            }
            catch (FileNotFoundException)
            {
                // Data file doesn't exist, nothing needs to be loaded since we init to defaults.
                return;
            }

            var data = JsonSerializer.Deserialize<InstanceData>(fileData)!;

            // Actually copy data over.
            _currentRevision = data.CurrentRevision;
        }

        private void SaveData()
        {
            var dataPath = Path.Combine(InstanceDir, "data.json");

            File.WriteAllText(dataPath, JsonSerializer.Serialize(new InstanceData
            {
                CurrentRevision = _currentRevision
            }));
        }

        public async Task StartAsync()
        {
            await _stateLock.WaitAsync();

            try
            {
                await StartLockedAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Exception while starting server.");
            }
            finally
            {
                _stateLock.Release();
            }
        }

        // You need to acquire _stateLock before calling this!!!
        private async Task StartLockedAsync()
        {
            _logger.LogDebug("{Key}: starting server", Key);

            _serverTimeoutTcs?.Cancel();

            if (_runningServerProcess != null)
            {
                throw new InvalidOperationException();
            }

            if (_updateOnRestart)
            {
                _updateOnRestart = false;

                if (_updateProvider != null)
                {
                    var hasUpdate = await _updateProvider.CheckForUpdateAsync(_currentRevision);
                    _logger.LogDebug("Update available: {available}.", hasUpdate);

                    if (hasUpdate)
                    {
                        var newRevision = await _updateProvider.RunUpdateAsync(
                            _currentRevision,
                            Path.Combine(InstanceDir, "bin"));
                        
                        if (newRevision != null)
                        {
                            _logger.LogDebug("Updated from {current} to {new}.",
                                _currentRevision ?? "<none>",
                                newRevision);

                            _loadFailCount = 0;
                            _currentRevision = newRevision;
                            SaveData();
                        }
                        else
                        {
                            _logger.LogError("Failed to update!");
                        }
                    }
                }
            }

            GenerateNewToken();

            _lastPing = null;

            _logger.LogTrace("Getting launch info...");

            var startInfo = new ProcessStartInfo
            {
                WorkingDirectory = InstanceDir,
                FileName = Path.Combine(InstanceDir, _instanceConfig.RunCommand),
                UseShellExecute = false,
                ArgumentList =
                {
                    // Watchdog comms config.
                    "--cvar", $"watchdog.token={Secret}",
                    "--cvar", $"watchdog.key={Key}",
                    "--cvar", $"watchdog.baseUrl={_configuration["BaseUrl"]}",

                    "--config-file", Path.Combine(InstanceDir, "config.toml"),
                    "--data-dir", Path.Combine(InstanceDir, "data"),
                }
            };

            foreach (var (envVar, value) in _instanceConfig.EnvironmentVariables)
            {
                startInfo.Environment[envVar] = value;
            }

            // Add current build information.
            if (_currentRevision != null && _updateProvider != null)
            {
                foreach (var (cVar, value) in _updateProvider.GetLaunchCVarOverrides(_currentRevision))
                {
                    startInfo.ArgumentList.Add("--cvar");
                    startInfo.ArgumentList.Add($"{cVar}={value}");
                }
            }

            _logger.LogTrace("Launching...");
            try
            {
                _runningServerProcess = Process.Start(startInfo);
                _logger.LogDebug("Launched! PID: {pid}", _runningServerProcess!.Id);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Exception while launching!");
            }

            // MonitorServerAsync will catch start exception and restart if necessary.
            _monitorTask = MonitorServerAsync();
        }

        private async Task MonitorServerAsync()
        {
            if (_runningServerProcess != null)
            {
                _logger.LogDebug("Starting to monitor server");

                var exitTask = _runningServerProcess.WaitForExitAsync();

                StartTimeoutTimer();

                await exitTask;

                _logger.LogInformation("{Key} shut down with exit code {ExitCode}", Key,
                    _runningServerProcess.ExitCode);
            }

            if (_shuttingDown)
            {
                return;
            }

            await _stateLock.WaitAsync();

            _serverTimeoutTcs?.Cancel();

            try
            {
                _runningServerProcess = null;

                if (_lastPing == null)
                {
                    // If the server shuts down before sending a ping we assume it crashed during init.
                    _loadFailCount += 1;
                    _logger.LogWarning("{Key} shut down before sending ping on attempt {attempt}", Key,
                        _loadFailCount);

                    if (_loadFailCount >= LoadFailMax)
                    {
                        _startupFailUpdateWait = true;
                        // Server keeps crashing during init, wait for an update to fix it.
                        _logger.LogWarning("{Key} is failing to start, giving up until update.", Key);
                        return;
                    }
                }
                else
                {
                    _loadFailCount = 0;
                }

                if (!_shuttingDown)
                {
                    await StartLockedAsync();
                }
            }
            finally
            {
                _stateLock.Release();
            }
        }

        private void GenerateNewToken()
        {
            Span<byte> raw = stackalloc byte[64];
            using var crypto = new RNGCryptoServiceProvider();
            crypto.GetBytes(raw);

            var token = Convert.ToBase64String(raw);
            Secret = token;
            _logger.LogTrace("Server token is {0}", token);

            if (_logger.IsEnabled(LogLevel.Trace))
            {
                var combined = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Key}:{token}"));
                _logger.LogTrace("Authorization: Basic {0}", combined);
            }

            _serverHttpClient.DefaultRequestHeaders.Remove("WatchdogToken");
            _serverHttpClient.DefaultRequestHeaders.Add("WatchdogToken", token);
        }

        public string InstanceDir =>
            Path.Combine(Environment.CurrentDirectory, _serversConfiguration.InstanceRoot, Key);

        public async Task ShutdownAsync(CancellationToken cancellationToken)
        {
            _shuttingDown = true;

            await _stateLock.WaitAsync(cancellationToken);
            try
            {
                if (_runningServerProcess != null)
                {
                    await ForceShutdownServerAsync(cancellationToken);

                    await _monitorTask!;
                }
            }
            catch (OperationCanceledException)
            {
                _runningServerProcess?.Kill(true);
            }
            finally
            {
                _stateLock.Release();
            }
        }

        public async void PingReceived()
        {
            await _stateLock.WaitAsync();
            try
            {
                _logger.LogTrace("Received ping from server.");
                _lastPing = DateTime.Now;

                StartTimeoutTimer();
            }
            finally
            {
                _stateLock.Release();
            }
        }

        private async void StartTimeoutTimer()
        {
            _serverTimeoutTcs?.Cancel();

            _serverTimeoutTcs = new CancellationTokenSource();

            var token = _serverTimeoutTcs.Token;

            try
            {
                _logger.LogTrace("Timeout timer started");
                await Task.Delay(PingTimeoutDelay, token);

                await _stateLock.WaitAsync(token);
            }
            catch (OperationCanceledException)
            {
                // It still lives.
                _logger.LogTrace("Timeout broken, it lives.");
                return;
            }

            try
            {
                TimeoutKill();
            }
            finally
            {
                _stateLock.Release();
            }
        }

        private void TimeoutKill()
        {
            _logger.LogWarning("{Key}: timed out, killing", Key);

            if (_runningServerProcess == null)
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
                        
                        var client = new DiagnosticsClient(_runningServerProcess.Id);
                        client.WriteDump(_instanceConfig.TimeoutDumpType, dumpFile);

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
            
            _runningServerProcess.Kill();
        }

        public void HandleUpdateCheck()
        {
            if (_updateProvider == null)
            {
                return;
            }

            _logger.LogDebug("Received update notification.");
            _taskQueue.QueueTask(async cancel =>
            {
                var updateAvailable = await _updateProvider.CheckForUpdateAsync(_currentRevision, cancel);
                _logger.LogTrace("Update available? {available}", updateAvailable);

                await _stateLock.WaitAsync(cancel);
                try
                {
                    _updateOnRestart = updateAvailable;
                    if (updateAvailable)
                    {
                        if (IsRunning)
                        {
                            _logger.LogTrace("Server is running, sending update notification.");
                            await SendUpdateNotificationAsync(cancel);
                        }
                        else if (_startupFailUpdateWait)
                        {
                            _startupFailUpdateWait = false;
                            await StartLockedAsync();
                        }
                    }
                }
                finally
                {
                    _stateLock.Release();
                }
            });
        }

        private async Task SendUpdateNotificationAsync(CancellationToken cancel = default)
        {
            var resp = await _serverHttpClient.PostAsync($"http://localhost:{_instanceConfig.ApiPort}/update", null!, cancel);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Bad HTTP status code on update notification: {status}", resp.StatusCode);
            }
        }

        public async Task DoRestartCommandAsync(CancellationToken cancel = default)
        {
            await _stateLock.WaitAsync(cancel);

            try
            {
                if (_runningServerProcess == null && _startupFailUpdateWait)
                {
                    _loadFailCount = 0;
                    _startupFailUpdateWait = false;
                    await StartLockedAsync();
                    return;
                }
            }
            finally
            {
                _stateLock.Release();
            }

            await ForceShutdownServerAsync(cancel);
        }

        public async Task ForceShutdownServerAsync(CancellationToken cancel = default)
        {
            var proc = _runningServerProcess;
            if (proc == null || proc.HasExited)
            {
                return;
            }

            try
            {
                await SendShutdownNotificationAsync(cancel);
            }
            catch (HttpRequestException e)
            {
                _logger.LogInformation(e, "Exception sending shutdown notification to server. Killing.");
                proc.Kill();
                return;
            }

            // Give it 5 seconds to shut down.
            await Task.WhenAny(proc.WaitForExitAsync(cancel), Task.Delay(5000, cancel));

            if (!proc.HasExited)
            {
                proc.Kill();
            }
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