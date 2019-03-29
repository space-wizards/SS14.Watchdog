using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Mono.Unix.Native;

namespace SS14.Watchdog
{
    internal sealed class Program
    {
        private Config _config;
        private CancellationTokenSource _cancelSleepToken;
        private SemaphoreSlim _cancelSemaphore;
        private ApiHost _apiHost;

        private ControlRequest _request;

        private bool _serverIsBroken = false;

        private Process _serverProcess;

        internal static async Task Main(string[] args)
        {
            var program = new Program();
            await program.Main();
        }

        internal async Task Main()
        {
            _config = Config.Load();
            _cancelSleepToken = new CancellationTokenSource();
            _cancelSemaphore = new SemaphoreSlim(1);
            _apiHost = new ApiHost(_config, this);
            _apiHost.Start();

            Console.WriteLine("Starting server initially.");
            await _doStartServer();

            while (true)
            {
                if (_request == ControlRequest.Update)
                {
                    await _doUpdateServer();
                }
                else if (_request == ControlRequest.Restart)
                {
                    Console.WriteLine("Restarting server.");
                    await _doShutdownServer();
                    await _doStartServer();
                }
                else if (_serverProcess?.HasExited != false && !_serverIsBroken)
                {
                    Console.WriteLine("Seems like the server crashed. Joy. Restarting.");
                    await _doStartServer();
                }

                _request = ControlRequest.None;
                try
                {
                    await Task.Delay(5000, _cancelSleepToken.Token);
                }
                catch (TaskCanceledException)
                {
                    Console.WriteLine("ding");
                    _cancelSleepToken = new CancellationTokenSource();
                    _cancelSemaphore.Release();
                }
            }
        }

        /// <summary>
        ///     Signal that the SS14 server should be restarted.
        /// </summary>
        public void Restart()
        {
            // TODO: Synchronization.
            if (_request < ControlRequest.Restart)
            {
                _request = ControlRequest.Restart;
                InterruptWait();
            }
        }

        /// <summary>
        ///     Signal that an update should be ran.
        /// </summary>
        public void RunUpdate()
        {
            if (_request < ControlRequest.Update)
            {
                _request = ControlRequest.Update;
                InterruptWait();
            }
        }

        private void InterruptWait()
        {
            _cancelSemaphore.Wait();
            _cancelSleepToken.Cancel();
        }

        private async Task _doUpdateServer()
        {
            Console.WriteLine("Updating server!");
            await _doShutdownServer();

            // Download new binary.

            Console.WriteLine("Downloading update binary.");
            var tmpFile = Path.GetTempFileName();
            using (var client = new WebClient())
            {
                await client.DownloadFileTaskAsync(_config.BinaryDownloadPath, tmpFile);
            }

            Console.WriteLine("Clearing installation directory.");
            // Delete contents of server bin.
            var dirInfo = new DirectoryInfo(_config.ServerBinPath);
            foreach (var file in dirInfo.EnumerateFiles())
            {
                file.Delete();
            }

            foreach (var dir in dirInfo.EnumerateDirectories())
            {
                dir.Delete(true);
            }

            Console.WriteLine("Unzipping.");
            // Unzip.
            ZipFile.ExtractToDirectory(tmpFile, _config.ServerBinPath);

            Console.WriteLine("Cleaning up.");
            // Delete zip file download.
            File.Delete(tmpFile);

            Console.WriteLine("Restarting server.");
            // Restart server.
            await _doStartServer();
        }

        private async Task _doShutdownServer()
        {
            Console.WriteLine("Shutting down server!");
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                Syscall.kill(_serverProcess.Id, Signum.SIGTERM);
                var task = _serverProcess.WaitForExitAsync();
                if (await Task.WhenAny(task, Task.Delay(5000)) != task)
                {
                    Console.WriteLine("Server did not shut down after SIGTERM. Killing.");
                    // Server is still running after 5 second grace period. Rip.
                    _serverProcess.Kill();
                    _serverProcess.WaitForExit();
                }
                Debug.Assert(_serverProcess.HasExited);
            }
            else
            {
                Console.WriteLine("Server already exited? {0}", _serverProcess?.HasExited);
            }

            _serverProcess = null;
            Console.WriteLine("Server shut down.");
        }

        private async Task _doStartServer()
        {
            Debug.Assert(_serverProcess == null || _serverProcess.HasExited);

            _serverIsBroken = false;

            // If the server refuses to start we try three times.
            for (var i = 0; i < 3; i++)
            {
                _serverProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = _config.ToRun,
                    UseShellExecute = false,
                });

                // Give it a sec to crash maybe.
                await Task.Delay(3000);

                if (_serverProcess != null && !_serverProcess.HasExited)
                {
                    Console.WriteLine("Server started successfully.");
                    // Server started successfully.
                    return;
                }

                if (i < 2)
                {
                    Console.WriteLine("Failed to start server. Trying again.");
                }
            }

            Console.WriteLine("Failed to start server. Giving up.");
            // Server is refusing to start. Uh oh.
            _serverIsBroken = true;
            _serverProcess = null;
        }

        private enum ControlRequest
        {
            None = 0,
            Restart = 1,
            Update = 2,
        }
    }
}