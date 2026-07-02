namespace SS14.Watchdog.Configuration.Updates
{
    /// <summary>
    /// Configuration for <see cref="SS14.Watchdog.Components.Updates.UpdateProviderGit"/>.
    /// </summary>
    public class UpdateProviderGitConfiguration
    {
        /// <summary>
        /// Git repository URL. This is the source repository URL, not the watchdog root BaseUrl.
        /// </summary>
        public string BaseUrl { get; set; } = null!;

        /// <summary>
        /// Git branch to fetch, reset to, and package.
        /// </summary>
        public string Branch { get; set; } = "master";

        /// <summary>
        /// Whether to build a hybrid ACZ server package. When enabled, the client zip is hosted by the status host,
        /// so operators do not need to expose watchdog's binaries endpoint.
        /// </summary>
        public bool HybridACZ { get; set; } = true;
    }
}
