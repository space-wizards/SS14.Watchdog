using System;
using System.Text;

namespace SS14.Watchdog.Utility
{
    public static class Base64Util
    {
        /// <summary>
        ///     Shortcut for converting the input to UTF-8 and then to Base64.
        /// </summary>
        public static string StringToUtf8Base64(string text)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        }

        /// <summary>
        ///     Shortcut for decoding base64 that contains UTF-8.
        /// </summary>
        /// <param name="base64"></param>
        /// <returns></returns>
        public static string Utf8Base64ToString(string base64)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
    }
}