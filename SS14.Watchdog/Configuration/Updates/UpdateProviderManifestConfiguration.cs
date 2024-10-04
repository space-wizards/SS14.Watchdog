namespace SS14.Watchdog.Configuration.Updates
{
    public class UpdateProviderManifestConfiguration
    {
        public string ManifestUrl { get; set; } = null!;
        public ManifestAuthenticationConfiguration? Authentication { get; set; }
    }

    public sealed class ManifestAuthenticationConfiguration
    {
        public string? Username { get; set; }
        public string? Password { get; set; }
    }
}
