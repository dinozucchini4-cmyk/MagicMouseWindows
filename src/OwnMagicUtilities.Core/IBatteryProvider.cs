namespace OwnMagicUtilities.Core;

/// <summary>
/// A hardware-specific battery source. Providers should be read-only and
/// should return explicit failure states instead of treating failures as 0%.
/// </summary>
public interface IBatteryProvider
{
    string Name { get; }

    bool CanRead(AppleDeviceIdentity device);

    ValueTask<BatteryReading> ReadAsync(
        AppleDeviceIdentity device,
        CancellationToken cancellationToken);
}

/// <summary>
/// Tries eligible providers in order. The first valid reading wins; if no
/// provider succeeds, the most informative failure is returned.
/// </summary>
public sealed class BatteryProviderRouter
{
    private readonly IReadOnlyList<IBatteryProvider> _providers;

    public BatteryProviderRouter(IEnumerable<IBatteryProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToArray();
    }

    public async ValueTask<BatteryReading> ReadAsync(
        AppleDeviceIdentity device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        var eligibleProviderFound = false;
        BatteryReading? bestFailure = null;

        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!provider.CanRead(device))
            {
                continue;
            }

            eligibleProviderFound = true;
            BatteryReading result;

            try
            {
                result = await provider.ReadAsync(device, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = BatteryReading.Error($"{provider.Name}: {ex.Message}");
            }

            if (result.Availability == BatteryAvailability.Available)
            {
                if (result.Percentage is >= 0 and <= 100)
                {
                    return result;
                }

                // Never allow a provider to label a missing or out-of-range
                // percentage as an available reading.
                result = BatteryReading.Unsupported(
                    $"{provider.Name}: returned an invalid or missing battery percentage.");
            }

            if (bestFailure is null || FailurePriority(result) > FailurePriority(bestFailure))
            {
                bestFailure = result;
            }
        }

        if (bestFailure is not null)
        {
            return bestFailure;
        }

        return eligibleProviderFound
            ? BatteryReading.Unavailable("All eligible battery providers failed.")
            : BatteryReading.NotDetected("No battery provider supports this device and transport.");
    }

    private static int FailurePriority(BatteryReading reading) => reading.Availability switch
    {
        BatteryAvailability.Error => 4,
        BatteryAvailability.Unsupported => 3,
        BatteryAvailability.Unavailable => 2,
        BatteryAvailability.NotDetected => 1,
        _ => 0
    };
}
