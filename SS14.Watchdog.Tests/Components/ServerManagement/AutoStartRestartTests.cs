using System;
using System.Data;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net.Http;
using Microsoft.Diagnostics.NETCore.Client;
using NUnit.Framework;
using SS14.Watchdog.Components.BackgroundTasks;
using SS14.Watchdog.Components.DataManagement;
using SS14.Watchdog.Components.Notifications;
using SS14.Watchdog.Components.ProcessManagement;
using SS14.Watchdog.Components.ServerManagement;
using SS14.Watchdog.Configuration;

namespace SS14.Watchdog.Tests.Components.ServerManagement
{
    [TestFixture]
    public sealed class AutoStartRestartTests
    {
        [Test]
        public async Task RestartStarts_AutoStartFalse_Instance()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"watchdog-test-{Guid.NewGuid()}.db");

            // Create DB with ServerInstance table and PersistedToken column.
            var connString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
            using (var conn = new SqliteConnection(connString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"CREATE TABLE ServerInstance(
    Key TEXT NOT NULL PRIMARY KEY,
    Revision TEXT NULL
);";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "ALTER TABLE ServerInstance ADD COLUMN PersistedToken TEXT;";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "ALTER TABLE ServerInstance ADD COLUMN PersistedPid INTEGER;";
                cmd.ExecuteNonQuery();
                // Insert a row for our instance key so LoadData() doesn't throw.
                cmd.CommandText = "INSERT INTO ServerInstance(Key, Revision) VALUES ('test', NULL);";
                cmd.ExecuteNonQuery();
            }

            var dataManager = new DataManager(NullLogger<DataManager>.Instance, Options.Create(new DataOptions { File = dbPath }));

            var instanceConfig = new InstanceConfiguration();
            instanceConfig.AutoStart = false;
            instanceConfig.ApiPort = 12345;

            var serversConfig = new ServersConfiguration { InstanceRoot = "." };

            var taskQueue = new BackgroundTaskQueue(NullLogger<BackgroundTaskQueue>.Instance);

            var procManager = new TestProcessManager();

            var notificationManager = new NotificationManager(new TestHttpFactory(), NullLogger<NotificationManager>.Instance, Options.Create(new NotificationOptions()));

            var svcProvider = new ServiceCollection().BuildServiceProvider();

            var instance = new ServerInstance("test", instanceConfig, new ConfigurationBuilder().Build(), serversConfig,
                NullLogger<ServerInstance>.Instance, taskQueue, svcProvider, dataManager, procManager, notificationManager);

            // Invoke private RunCommandRestart to exercise restart path synchronously.
            var method = typeof(ServerInstance).GetMethod("RunCommandRestart", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)method.Invoke(instance, new object[] { CancellationToken.None })!;

            Assert.That(procManager.StartCalled, Is.True, "StartServer should be called on Restart even if AutoStart=false");
        }

        [Test]
        public async Task RestartCommandThroughQueueStartsInstance()
        {
            var dbPath = Path.Combine(Path.GetTempPath(), $"watchdog-test-{Guid.NewGuid()}.db");

            var connString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
            using (var conn = new SqliteConnection(connString))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"CREATE TABLE ServerInstance(
    Key TEXT NOT NULL PRIMARY KEY,
    Revision TEXT NULL
);";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "ALTER TABLE ServerInstance ADD COLUMN PersistedToken TEXT;";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "ALTER TABLE ServerInstance ADD COLUMN PersistedPid INTEGER;";
                cmd.ExecuteNonQuery();

                cmd.CommandText = "INSERT INTO ServerInstance(Key, Revision) VALUES ('test', NULL);";
                cmd.ExecuteNonQuery();
            }

            var dataManager = new DataManager(NullLogger<DataManager>.Instance, Options.Create(new DataOptions { File = dbPath }));

            var instanceConfig = new InstanceConfiguration();
            instanceConfig.AutoStart = false;
            instanceConfig.ApiPort = 12345;

            var serversConfig = new ServersConfiguration { InstanceRoot = "." };

            var taskQueue = new BackgroundTaskQueue(NullLogger<BackgroundTaskQueue>.Instance);

            var procManager = new TestProcessManager();

            var notificationManager = new NotificationManager(new TestHttpFactory(), NullLogger<NotificationManager>.Instance, Options.Create(new NotificationOptions()));

            var svcProvider = new ServiceCollection().BuildServiceProvider();

            var instance = new ServerInstance("test", instanceConfig, new ConfigurationBuilder().Build(), serversConfig,
                NullLogger<ServerInstance>.Instance, taskQueue, svcProvider, dataManager, procManager, notificationManager);

            // Simulate that the instance is currently stopped and not running.
            var stoppedField = typeof(ServerInstance).GetField("_stopped", BindingFlags.Instance | BindingFlags.NonPublic)!;
            stoppedField.SetValue(instance, true);

            using var cts = new CancellationTokenSource();
            // Run command loop in background until we cancel.
            var cmdLoopMethod = typeof(ServerInstance).GetMethod("CommandLoop", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var loopTask = (Task)cmdLoopMethod.Invoke(instance, new object[] { cts.Token })!;

            // Send restart via public API which queues CommandRestart.
            await instance.DoRestartCommandAsync(CancellationToken.None);

            // Give it a short moment to process.
            await Task.Delay(200);

            // Stop the loop and await completion.
            cts.Cancel();
            try { await loopTask; } catch (OperationCanceledException) { }

            Assert.That(procManager.StartCalled, Is.True, "StartServer should be called when Restart is queued");
        }
        private sealed class TestHttpFactory : IHttpClientFactory
        {
            public System.Net.Http.HttpClient CreateClient(string name) => new System.Net.Http.HttpClient();
        }

        private sealed class TestProcessManager : IProcessManager
        {
            public bool StartCalled { get; private set; }

            public bool CanPersist => false;

            public Task<IProcessHandle> StartServer(IServerInstance instance, ProcessStartData data, CancellationToken cancel = default)
            {
                StartCalled = true;
                return Task.FromResult<IProcessHandle>(new DummyHandle());
            }

            public Task<IProcessHandle?> TryGetPersistedServer(IServerInstance instance, string program, CancellationToken cancel)
            {
                return Task.FromResult<IProcessHandle?>(null);
            }

            private sealed class DummyHandle : IProcessHandle
            {
                public void DumpProcess(string file, DumpType type) { }
                public Task WaitForExitAsync(CancellationToken cancel = default) => Task.CompletedTask;
                public Task Kill() => Task.CompletedTask;
                public Task<ProcessExitStatus?> GetExitStatusAsync() => Task.FromResult<ProcessExitStatus?>(null);
            }
        }
    }
}
