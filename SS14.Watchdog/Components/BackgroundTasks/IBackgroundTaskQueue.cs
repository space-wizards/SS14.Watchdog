using System;
using System.Threading;
using System.Threading.Tasks;

namespace SS14.Watchdog.Components.BackgroundTasks
{
    public interface IBackgroundTaskQueue
    {
        void QueueTask(Func<CancellationToken, Task> task);
    }
}