using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SS14.Watchdog.Components.Notifications;

/// <summary>
/// Implements external notifications to various services.
/// </summary>
/// <seealso cref="NotificationOptions"/>
public sealed partial class NotificationManager(
    IHttpClientFactory http,
    ILogger<NotificationManager> logger,
    IOptions<NotificationOptions> options)
{
    public const string DiscordHttpClient = "discord_webhook";

    private readonly HttpClient _httpClient = http.CreateClient();

    // I can't wait for this interface to keep expanding in the future.
    public void SendNotification(string message)
    {
        var optionsValue = options.Value;
        if (optionsValue.DiscordWebhook == null)
        {
            logger.LogTrace("Not sending notification: no Discord webhook URL configured");
            return;
        }

        SendWebhook(optionsValue, message);
    }

    private async void SendWebhook(NotificationOptions optionsValue, string message)
    {
        logger.LogTrace("Sending notification \"{Message}\" to Discord webhook...", message);

        try
        {
            var messageObject = new DiscordWebhookExecute
            {
                Content = message,
                AllowedMentions = DiscordAllowedMentions.None
            };

            using var response = await _httpClient.PostAsJsonAsync(
                optionsValue.DiscordWebhook,
                messageObject,
                DiscordSourceGenerationContext.Default.DiscordWebhookExecute);

            response.EnsureSuccessStatusCode();

            logger.LogTrace("Succeeded sending notification to Discord webhook");
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while sending Discord webhook!");
        }
    }

    private sealed class DiscordWebhookExecute
    {
        public string? Content { get; set; }
        public DiscordAllowedMentions? AllowedMentions { get; set; } = null;
    }

    private sealed class DiscordAllowedMentions
    {
        public static readonly DiscordAllowedMentions None = new() { Parse = [] };

        public string[]? Parse { get; set; }
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.KebabCaseLower)]
    [JsonSerializable(typeof(DiscordWebhookExecute))]
    private partial class DiscordSourceGenerationContext : JsonSerializerContext;
}
