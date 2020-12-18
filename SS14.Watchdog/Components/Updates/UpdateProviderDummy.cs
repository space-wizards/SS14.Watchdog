using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace SS14.Watchdog.Components.Updates
{
    /// <summary>
    ///     Pretends an update is always available.
    /// </summary>
    public sealed class UpdateProviderDummy : UpdateProvider
    {
        public override Task<bool> CheckForUpdateAsync(string? currentVersion, CancellationToken cancel = default)
        {
            return Task.FromResult(true);
        }

        public override Task<string?> RunUpdateAsync(string? currentVersion, string binPath, CancellationToken cancel = default)
        {
            string newNumber;
            if (currentVersion == null)
            {
                newNumber = "1";
            }
            else
            {
                var p = int.Parse(currentVersion, CultureInfo.InvariantCulture);

                newNumber = $"{p + 1}";
            }

            return Task.FromResult<string?>(newNumber);
        }
    }
}