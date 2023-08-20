namespace SS14.Watchdog.Components.DataManagement;

/// <summary>
/// Options for the data management system of the watchdog itself.
/// </summary>
/// <seealso cref="DataManager"/>
public sealed class DataOptions
{
    public const string Position = "Data";

    /// <summary>
    /// Which file to store persistent data in, relative to the working directory.
    /// </summary>
    public string File { get; set; } = "data.db";
}