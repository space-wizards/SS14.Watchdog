using NUnit.Framework;
using SS14.Watchdog.Utility;

namespace SS14.Watchdog.Tests.Utility
{
    public class Base64UtilTest
    {
        [Test]
        public void TestRoundtrip()
        {
            const string message = "Secure that disk!";

            var trip = Base64Util.Utf8Base64ToString(Base64Util.StringToUtf8Base64(message));

            Assert.That(trip, Is.EqualTo(message));
        }
    }
}