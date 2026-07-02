using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;

namespace SS14.Watchdog.Utility
{
    public static class AuthorizationUtility
    {
        public static bool TryParseBasicAuthentication(string authorization,
            [NotNullWhen(false)] out IActionResult? failure,
            [NotNullWhen(true)] out string? username,
            [NotNullWhen(true)] out string? password)
        {
            username = null;
            password = null;

            if (!authorization.StartsWith("Basic "))
            {
                failure = new UnauthorizedResult();
                return false;
            }

            string decoded;
            try
            {
                decoded = Base64Util.Utf8Base64ToString(authorization[6..]);
            }
            catch (FormatException)
            {
                failure = new BadRequestResult();
                return false;
            }

            var split = decoded.Split(':');

            if (split.Length != 2)
            {
                failure = new BadRequestResult();
                return false;
            }

            username = split[0];
            password = split[1];
            failure = null;
            return true;
        }

    }
}
