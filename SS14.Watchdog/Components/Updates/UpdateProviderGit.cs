using System.Threading;
using System.Threading.Tasks;

namespace SS14.Watchdog.Components.Updates
{
    public class UpdateProviderGit : UpdateProvider
    {
        public override Task<bool> CheckForUpdateAsync(string? currentVersion, CancellationToken cancel = default)
        {
        }

        public override Task<RevisionDescription?> RunUpdateAsync(string? currentVersion, string binPath, CancellationToken cancel = default)
        {
        }
    }
}