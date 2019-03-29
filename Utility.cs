using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SS14.Watchdog
{
    public static class Utility
    {
        public static Task WaitForExitAsync(this Process process, CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<object>();
            process.EnableRaisingEvents = true;
            process.Exited += (sender, args) => tcs.TrySetResult(null);
            if (cancellationToken != default)
                cancellationToken.Register(tcs.SetCanceled);

            if (process.HasExited)
            {
                tcs.TrySetResult(null);
            }

            return tcs.Task;
        }
    }
}