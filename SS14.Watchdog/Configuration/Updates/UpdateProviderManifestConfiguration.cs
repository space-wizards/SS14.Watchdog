namespace SS14.Watchdog.Configuration.Updates
{
    /// <summary>
    /// Configuration for <see cref="SS14.Watchdog.Components.Updates.UpdateProviderManifest"/>.
    /// </summary>
    public class UpdateProviderManifestConfiguration
    {
        /// <summary>
        /// URL of the SS14 build manifest JSON to fetch.
        /// </summary>
        public string ManifestUrl { get; set; } = null!;

        /// <summary>
        /// Optional basic authentication credentials for fetching the manifest and referenced artifacts.
        /// </summary>
        public ManifestAuthenticationConfiguration? Authentication { get; set; }
    }

    /// <summary>
    /// Basic authentication credentials for manifest update downloads.
    /// </summary>
    public sealed class ManifestAuthenticationConfiguration
    {
        /// <summary>
        /// Basic authentication username.
        /// </summary>
        public string? Username { get; set; }

        /// <summary>
        /// Basic authentication password.
        /// </summary>
        public string? Password { get; set; }
    }
}
