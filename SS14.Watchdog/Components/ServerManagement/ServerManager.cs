using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SS14.Watchdog.Components.BackgroundTasks;
using SS14.Watchdog.Components.DataManagement;
using SS14.Watchdog.Components.Notifications;
using SS14.Watchdog.Components.ProcessManagement;
using SS14.Watchdog.Configuration;

namespace SS14.Watchdog.Components.ServerManagement
{
    [UsedImplicitly]
    public sealed class ServerManager : BackgroundService, IServerManager
    {
        private readonly ILogger<ServerManager> _logger;
        private readonly IBackgroundTaskQueue _taskQueue;
        private readonly IServiceProvider _provider;
        private readonly DataManager _dataManager;
        private readonly IProcessManager _processManager;
        private readonly IServer _server;
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        private readonly NotificationManager _notificationManager;
        private readonly IConfiguration _configuration;
        private readonly IOptionsMonitor<ServersConfiguration> _serverCfg;
        private readonly Dictionary<string, ServerInstance> _instances = new Dictionary<string, ServerInstance>();

        public IReadOnlyCollection<IServerInstance> Instances => _instances.Values;

        public ServerManager(
            IOptionsMonitor<ServersConfiguration> instancesOptions,
            ILogger<ServerManager> logger,
            IConfiguration configuration,
            IBackgroundTaskQueue taskQueue,
            IServiceProvider provider,
            DataManager dataManager,
            IProcessManager processManager,
            IServer server,
            IHostApplicationLifetime hostApplicationLifetime,
            NotificationManager notificationManager)
        {
            _logger = logger;
            _configuration = configuration;
            _taskQueue = taskQueue;
            _provider = provider;
            _dataManager = dataManager;
            _processManager = processManager;
            _server = server;
            _hostApplicationLifetime = hostApplicationLifetime;
            _notificationManager = notificationManager;
            _serverCfg = instancesOptions;
        }

        public bool TryGetInstance(string key, [NotNullWhen(true)] out IServerInstance? instance)
        {
            var ret = _instances.TryGetValue(key, out var serverInstance);

            instance = serverInstance;
            return ret;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // This gets ran in parallel with host init.

            // Wait for web application to complete, so we know the watchdog's local URL.
            await WaitForApplicationStart().WaitAsync(stoppingToken);

            var baseAddress = GetBestBaseAddress();
            _logger.LogDebug("Base address for watchdog is {BaseAddress}", baseAddress);

            _logger.LogInformation("Starting server instances...");

            var tasks = new List<Task>();

            // Start server instances in background while main host loads.
            foreach (var instance in _instances.Values)
            {
                tasks.Add(instance.StartAsync(baseAddress, stoppingToken));
            }

            await Task.WhenAll(tasks);
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _serverCfg.OnChange(CfgChanged);

            var options = _serverCfg.CurrentValue;
            var instanceRoot = Path.Combine(Environment.CurrentDirectory, options.InstanceRoot);

            if (!Directory.Exists(instanceRoot))
            {
                Directory.CreateDirectory(instanceRoot);
                _logger.LogInformation("Created InstanceRoot {InstanceRoot}", instanceRoot);
            }

            // Populate database with server instances.
            using (var con = _dataManager.OpenConnection())
            {
                var tx = con.BeginTransaction();

                foreach (var key in options.Instances.Keys)
                {
                    con.Execute("INSERT OR IGNORE INTO ServerInstance (Key) VALUES (@Key)", new { Key = key });
                }

                tx.Commit();
            }

            // Init server instances.
            foreach (var (key, instanceOptions) in options.Instances)
            {
                _logger.LogDebug("Initializing instance {Name} ({Key})", instanceOptions.Name, key);

                var instance =
                    new ServerInstance(
                        key,
                        instanceOptions,
                        _configuration,
                        options,
                        _provider.GetRequiredService<ILogger<ServerInstance>>(),
                        _taskQueue,
                        _provider,
                        _dataManager,
                        _processManager,
                        _notificationManager);

                _instances.Add(key, instance);
            }

            // This calls ExecuteAsync
            await base.StartAsync(cancellationToken);
        }

        private void CfgChanged(ServersConfiguration obj)
        {
            foreach (var (k, instance) in _instances)
            {
                if (!obj.Instances.TryGetValue(k, out var instanceCfg))
                    return;

                instance.OnConfigUpdate(instanceCfg);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                await base.StopAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Ah heck. We're shutting down forcefully.
                // At least try to kill the server processes I guess (if necessary).
                foreach (var instance in _instances.Values)
                {
                    instance.ForceShutdown();
                }
            }
        }

        private Task WaitForApplicationStart()
        {
            var tcs = new TaskCompletionSource();
            _hostApplicationLifetime.ApplicationStarted.Register(() => tcs.SetResult());
            return tcs.Task;
        }

        private string GetBestBaseAddress()
        {
            // Prefer http:// and localhost URLs, in case multiple are bound.
            // In the future, we will probably want to support Unix sockets.
            return _server.Features.Get<IServerAddressesFeature>()!.Addresses
                .Select(x => new { Address = x, Uri = new Uri(x) })
                .OrderBy(x => x.Uri.Scheme switch
                {
                    "http" => 1,
                    _ => 2
                })
                .ThenBy(x => x.Uri.Host switch
                {
                    "localhost" => 1,
                    _ => 2
                })
                .Select(x => x.Address)
                .First();
        }
    }
}
