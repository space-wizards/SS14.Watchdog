using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using SS14.Watchdog.Components.ProcessManagement;
using SS14.Watchdog.Components.ServerManagement;

namespace SS14.Watchdog.Tests.Components.ServerManagement;

[TestFixture]
public sealed class ServerInstanceTests
{
    [Test]
    public async Task RepeatedStopCommandForcesShutdown()
    {
        var instance = (ServerInstance)RuntimeHelpers.GetUninitializedObject(typeof(ServerInstance));
        var process = new TestProcessHandle();

        SetField(instance, "_runningServer", process);
        SetField(instance, "_stopped", true);
        SetField(instance, "_logger", NullLogger<ServerInstance>.Instance);
        SetField(instance, "_serverHttpClient", new HttpClient(new StubHttpMessageHandler()));

        var method = typeof(ServerInstance).GetMethod("RunCommandStop", BindingFlags.Instance | BindingFlags.NonPublic)!;

        await (Task)method.Invoke(instance, [new ServerInstanceStopCommand(), CancellationToken.None])!;

        Assert.That(process.KillCount, Is.EqualTo(1));
    }

    private static void SetField<T>(object target, string fieldName, T value)
    {
        typeof(ServerInstance).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    }

    private sealed class TestProcessHandle : IProcessHandle
    {
        public int KillCount { get; private set; }

        public void DumpProcess(string file, DumpType type)
        {
        }

        public Task WaitForExitAsync(CancellationToken cancel = default)
        {
            throw new OperationCanceledException();
        }

        public Task Kill()
        {
            KillCount++;
            return Task.CompletedTask;
        }

        public Task<ProcessExitStatus?> GetExitStatusAsync()
        {
            return Task.FromResult<ProcessExitStatus?>(null);
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
