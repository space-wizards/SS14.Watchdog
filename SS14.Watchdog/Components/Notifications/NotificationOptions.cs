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
}
