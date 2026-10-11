using OwnMagicUtilities.Core;

namespace MagicMouseApp.Hardware;

public sealed record BatteryStatusItem(
    AppleDeviceKind Kind,
    string DeviceName,
    BatteryReading Reading);

/// <summary>
/// Polls each configured Apple device through the battery-provider router.
/// Reads are sequential to avoid overlapping requests to the same HID stack.
/// </summary>
public sealed class BatteryMonitorService
{
    private readonly BatteryProviderRouter _router;

    private static readonly AppleDeviceIdentity[] Targets =
    [
        new(
            AppleDeviceIdentity.AppleBluetoothVendorId,
            AppleDeviceIdentity.MagicMouse2015ProductId,
            AppleDeviceKind.MagicMouse2015,
            AppleTransport.Bluetooth,
            "Magic Mouse 2015"),
        new(
            AppleDeviceIdentity.AppleBluetoothVendorId,
            AppleDeviceIdentity.MagicTrackpad2ProductId,
            AppleDeviceKind.MagicTrackpad2,
            AppleTransport.Bluetooth,
            "Magic Trackpad 2"),
        new(
            AppleDeviceIdentity.AppleBluetoothVendorId,
            AppleDeviceIdentity.MagicKeyboardProductId,
            AppleDeviceKind.MagicKeyboard,
            AppleTransport.Bluetooth,
            "Magic Keyboard")
    ];

    public BatteryMonitorService()
    {
        _router = new BatteryProviderRouter([new WindowsHidBatteryProvider()]);
    }

    public async Task<IReadOnlyList<BatteryStatusItem>> ReadAllAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<BatteryStatusItem>(Targets.Length);
        foreach (var target in Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reading = await _router.ReadAsync(target, cancellationToken).ConfigureAwait(false);
            results.Add(new BatteryStatusItem(target.Kind, target.SourceId, reading));
        }

        return results;
    }
}
