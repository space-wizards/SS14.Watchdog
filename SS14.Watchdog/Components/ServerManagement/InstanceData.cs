using SS14.Watchdog.Components.Updates;

namespace SS14.Watchdog.Components.ServerManagement
{
    // Data that gets stored by the instance in data.json

    public sealed class InstanceData
    {
        public RevisionDescription? CurrentRevision { get; set; }
    }
}