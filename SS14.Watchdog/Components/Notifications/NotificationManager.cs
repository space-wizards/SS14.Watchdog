using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
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
    public const string NotificationHttpClient = "watchdog_notification";

    private readonly HttpClient _httpClient = http.CreateClient();

    // I can't wait for this interface to keep expanding in the future.
    public void SendNotification(string message)
    {
        var optionsValue = options.Value;
        if (optionsValue.DiscordWebhook == null)
        {
            logger.LogTrace("Not sending discord notification: no Discord webhook URL configured");
            return;
        }

        SendWebhook(optionsValue, message);
    }

    public async void SendHttpNotification(string instanceId)
    {
        var optionsValue = options.Value;

        if (optionsValue.NotifyUrls.Count == 0)
        {
            logger.LogTrace("Not sending HTTP notification: no HTTP URLs configured");
            return;
        }

        await SendUpdatePosts(optionsValue, instanceId);
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

    private async Task SendUpdatePosts(NotificationOptions optionsValue, string instanceId)
    {
        logger.LogTrace("Sending update notification post...");

        try
        {
            var data = new UpdatePostExecute
            {
                InstanceId = instanceId
            };

            foreach (var urlString in optionsValue.NotifyUrls)
            {
                await SendSpecificUpdatePost(optionsValue, data, new Uri(urlString));
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while sending update notification post!");
        }
    }

    private async Task SendSpecificUpdatePost(NotificationOptions optionsValue, UpdatePostExecute data, Uri url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);

        if (!string.IsNullOrWhiteSpace(optionsValue.UpdatePostUser)
            && !string.IsNullOrWhiteSpace(optionsValue.UpdatePostToken))
        {
            var authAsBytes = Encoding.ASCII.GetBytes(optionsValue.UpdatePostUser + ":"  + optionsValue.UpdatePostToken);
            var authAsBase64 = Convert.ToBase64String(authAsBytes);
            var authHeader = new AuthenticationHeaderValue("Basic", authAsBase64);
            request.Headers.Authorization = authHeader;
        }

        request.Content = JsonContent.Create(data);

        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        logger.LogTrace("Succeeded sending update post");
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

    private sealed class UpdatePostExecute
    {
        /// <summary>
        ///     The Watchdog instance that was updated.
        /// </summary>
        public required string InstanceId { get; set; }
    }
}
