using System.Collections.Generic;

namespace SS14.Watchdog.Components.Notifications;

/// <summary>
/// Options for notifications the watchdog can send via various channels.
/// </summary>
/// <seealso cref="NotificationManager"/>
public sealed class NotificationOptions
{
    public const string Position = "Notification";

    /// <summary>
    /// A Discord webhook URL to send notifications like server crashes to.
    /// </summary>
    public string? DiscordWebhook { get; set; }

    /// <summary>
    ///     A list of URLs that should be sent a POST request.
    ///     If specified, each one will be provided <see cref="UpdatePostToken"/> as authentication.
    /// </summary>
    /// <remarks>No suffixes or prefixes are added to the URL before sending the POST.</remarks>
    public List<string> NotifyUrls { get; set; } = [];

    /// <summary>
    ///     The username that will be passed through to each update URL as BasicAuthentication.
    /// </summary>
    public string UpdatePostUser { get; set; } = string.Empty;

    /// <summary>
    ///     The token that will be passed through to each update URL as BasicAuthentication.
    /// </summary>
    public string UpdatePostToken { get; set; } = string.Empty;
}
