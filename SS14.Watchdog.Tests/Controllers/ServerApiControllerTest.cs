using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NUnit.Framework;
using SS14.Watchdog.Components.ServerManagement;
using SS14.Watchdog.Controllers;

namespace SS14.Watchdog.Tests.Controllers;

public class ServerApiControllerTest
{
    [Test]
    public void TestAuthorizationSuccess()
    {
        // Arrange
        const string secret = "Secure that disk!";
        const string key = "foo";

        var instanceMock = new Mock<IServerInstance>();
        instanceMock.SetupGet(p => p.Secret).Returns(secret);
        instanceMock.SetupGet(p => p.Key).Returns(key);

        var instance = instanceMock.Object;
        var manager = new Mock<IServerManager>();
        manager.Setup(i => i.TryGetInstance(key, out instance)).Returns(true);

        var controller = new ServerApiController(manager.Object);

        var authCorrect = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{key}:{secret}"));
        var authWrong = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{key}:oof"));

        // Act
        var success = controller.TryAuthorize(authCorrect, key, out var noFailure, out var correctInstance);
        var expectFail = controller.TryAuthorize(authWrong, key, out var failure, out var failInstance);

        // Assert
        Assert.That(success);
        Assert.That(correctInstance, Is.EqualTo(instanceMock.Object));

        Assert.That(expectFail, Is.False);
        Assert.That(failure, Is.Not.Null);
    }

    [Test]
    public void TestAuthorizationMalformed()
    {
        // Arrange
        var manager = Mock.Of<IServerManager>();
        var controller = new ServerApiController(manager);

        // Act
        var success = controller.TryAuthorize("Foobar", "honk", out var failure, out var instance);

        // Assert
        Assert.That(success, Is.False);
    }

    [Test]
    public void TestAuthorizationMissingHeader()
    {
        // Arrange
        var manager = Mock.Of<IServerManager>();
        var controller = new ServerApiController(manager);

        // Act
        var success = controller.TryAuthorize(null!, "honk", out var failure, out var instance);

        // Assert
        Assert.That(success, Is.False);
        Assert.That(failure, Is.InstanceOf<UnauthorizedResult>());
        Assert.That(instance, Is.Null);
    }

    [Test]
    public void TestAuthorizationInvalidBase64()
    {
        // Arrange
        var manager = Mock.Of<IServerManager>();
        var controller = new ServerApiController(manager);

        // Act
        var success = controller.TryAuthorize("Basic definitely not base64", "honk", out var failure, out var instance);

        // Assert
        Assert.That(success, Is.False);
        Assert.That(failure, Is.InstanceOf<BadRequestResult>());
        Assert.That(instance, Is.Null);
    }

    [Test]
    public async Task TestPing()
    {
        // Arrange
        const string secret = "Secure that disk!";
        const string key = "foo";

        var instanceMock = new Mock<IServerInstance>();
        instanceMock.SetupGet(p => p.Secret).Returns(secret);
        instanceMock.SetupGet(p => p.Key).Returns(key);

        var instance = instanceMock.Object;
        var manager = new Mock<IServerManager>();
        manager.Setup(i => i.TryGetInstance(key, out instance)).Returns(true);

        var controller = new ServerApiController(manager.Object);
        var authCorrect = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{key}:{secret}"));

        // Act
        var response = await controller.PingAsync(authCorrect, key);

        // Assert
        Assert.That(response, Is.InstanceOf<OkResult>());
        instanceMock.Verify(i => i.PingReceived(), Times.Once);
    }
}
