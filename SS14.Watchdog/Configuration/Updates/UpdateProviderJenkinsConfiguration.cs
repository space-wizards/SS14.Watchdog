namespace SS14.Watchdog.Configuration.Updates
{
    /// <summary>
    /// Configuration for <see cref="SS14.Watchdog.Components.Updates.UpdateProviderJenkins"/>.
    /// </summary>
    public sealed class UpdateProviderJenkinsConfiguration
    {
        /// <summary>
        /// Base URL of the Jenkins instance, without a trailing job path.
        /// </summary>
        public string BaseUrl { get; set; } = null!;

        /// <summary>
        /// Jenkins job name to query for the last successful build.
        /// </summary>
        public string JobName { get; set; } = null!;
    }
}
