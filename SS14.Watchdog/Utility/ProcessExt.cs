using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SS14.Watchdog.Utility
{
    public static class ProcessExt
    {
        // https://stackoverflow.com/a/50461641/4678631
        public static async Task WaitForExitAsync(this Process process, CancellationToken cancellationToken = default)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void ProcessExited(object? sender, EventArgs e)
            {
                tcs.TrySetResult(true);
            }

            process.EnableRaisingEvents = true;
            process.Exited += ProcessExited;

            try
            {
                if (process.HasExited)
                {
                    return;
                }

                await using (cancellationToken.Register(() => tcs.TrySetCanceled()))
                {
                    await tcs.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                process.Exited -= ProcessExited;
            }
        }
    }
}