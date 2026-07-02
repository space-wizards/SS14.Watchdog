namespace SS14.Watchdog.Configuration.Updates
{
    /// <summary>
    /// Configuration for <see cref="SS14.Watchdog.Components.Updates.UpdateProviderLocal"/>.
    /// </summary>
    public sealed class UpdateProviderLocalConfiguration
    {
        /// <summary>
        /// Version string that watchdog records as the active revision when local files should be considered updated.
        /// Change this value after replacing files externally to make watchdog restart into the new revision.
        /// </summary>
        public string CurrentVersion { get; set; } = null!;
    }
}
