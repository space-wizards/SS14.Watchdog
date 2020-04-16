using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SS14.Watchdog.Components.BackgroundTasks;
using SS14.Watchdog.Components.Updates;
using SS14.Watchdog.Configuration;

namespace SS14.Watchdog.Components.ServerManagement
{
    [UsedImplicitly]
    public sealed class ServerManager : BackgroundService, IServerManager
    {
        private readonly ILogger<ServerManager> _logger;
        private readonly ILogger<UpdateProviderJenkins> _jenkinsLogger;
        private readonly ILogger<UpdateProviderLocal> _localLogger;
        private readonly ILogger<ServerInstance> _serverInstanceLogger;
        private readonly IBackgroundTaskQueue _taskQueue;
        private readonly IConfiguration _configuration;
        private readonly ServersConfiguration _serversOptions;
        private readonly Dictionary<string, ServerInstance> _instances = new Dictionary<string, ServerInstance>();

        public IReadOnlyCollection<IServerInstance> Instances => _instances.Values;

        public ServerManager(
            IOptions<ServersConfiguration> instancesOptions,
            ILogger<ServerManager> logger, IConfiguration configuration, ILogger<UpdateProviderJenkins> jenkinsLogger,
            ILogger<ServerInstance> serverInstanceLogger, IBackgroundTaskQueue taskQueue,
            ILogger<UpdateProviderLocal> localLogger)
        {
            _logger = logger;
            _configuration = configuration;
            _jenkinsLogger = jenkinsLogger;
            _serverInstanceLogger = serverInstanceLogger;
            _taskQueue = taskQueue;
            _localLogger = localLogger;
            _serversOptions = instancesOptions.Value;
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

            var tasks = new List<Task>();

            // Start server instances in background while main host loads.
            foreach (var instance in _instances.Values)
            {
                tasks.Add(instance.StartAsync());
            }

            await Task.WhenAll(tasks);
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            // Init server instances.
            foreach (var (key, instanceOptions) in _serversOptions.Instances)
            {
                _logger.LogDebug("Initializing instance {Name} ({Key})", instanceOptions.Name, key);

                var instance =
                    new ServerInstance(key, instanceOptions, _configuration, _jenkinsLogger, _serversOptions,
                        _serverInstanceLogger, _taskQueue, _localLogger);
                _instances.Add(key, instance);
            }

            // This calls ExecuteAsync
            await base.StartAsync(cancellationToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await Task.WhenAll(_instances.Values.Select(i => i.ShutdownAsync(cancellationToken)));
        }
    }
}