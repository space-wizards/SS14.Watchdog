using System;
using System.Text;
using Moq;
using NUnit.Framework;
using SS14.Watchdog.Components.ServerManagement;
using SS14.Watchdog.Controllers;

namespace SS14.Watchdog.Tests.Controllers
{
    public class ServerApiControllerTest
    {
        [Test]
        public void TestAuthorizationSuccess()
        {
            // Arrange
            const string secret = "secure that disk";
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
        public void TestPing()
        {

        }
    }
}