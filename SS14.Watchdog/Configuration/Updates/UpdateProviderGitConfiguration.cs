namespace SS14.Watchdog.Configuration.Updates
{
    public class UpdateProviderGitConfiguration
    {
        public string BaseUrl { get; set; } = null!;
        public string Branch { get; set; } = "master";
    }
}