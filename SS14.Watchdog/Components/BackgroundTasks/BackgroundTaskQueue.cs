using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SS14.Watchdog.Components.BackgroundTasks
{
    // TODO: I copied this thing from ASP.NET's documentation but it's crap.
    // Doesn't run background tasks in parallel which is ridiculous!

    [UsedImplicitly]
    public class BackgroundTaskQueue : BackgroundService, IBackgroundTaskQueue
    {
        private readonly ILogger<BackgroundTaskQueue> _logger;

        private readonly ConcurrentQueue<Func<CancellationToken, Task>> _queue =
            new ConcurrentQueue<Func<CancellationToken, Task>>();

        private readonly SemaphoreSlim _tasksAvailableSemaphore = new SemaphoreSlim(0);

        public BackgroundTaskQueue(ILogger<BackgroundTaskQueue> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await _tasksAvailableSemaphore.WaitAsync(stoppingToken);

                _queue.TryDequeue(out var task);

                try
                {
                    await task!(stoppingToken);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Exception inside background task.");
                }
            }
        }

        public void QueueTask(Func<CancellationToken, Task> task)
        {
            _queue.Enqueue(task);
            _tasksAvailableSemaphore.Release();
        }
    }
}