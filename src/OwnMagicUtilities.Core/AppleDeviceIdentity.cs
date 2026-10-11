using System.Globalization;
using System.Text.RegularExpressions;

namespace OwnMagicUtilities.Core;

public enum AppleDeviceKind
{
    Unknown = 0,
    MagicMouse2015,
    MagicTrackpad2,
    MagicKeyboard
}

public enum AppleTransport
{
    Unknown = 0,
    Bluetooth,
    Usb
}

/// <summary>
/// A normalized identity for the Apple devices supported by this project.
/// Only documented vendor/product IDs are classified; device names are not
/// used as an identity check.
/// </summary>
public sealed record AppleDeviceIdentity(
    ushort VendorId,
    ushort ProductId,
    AppleDeviceKind Kind,
    AppleTransport Transport,
    string SourceId)
{
    public const ushort AppleBluetoothVendorId = 0x004C;
    public const ushort AppleUsbVendorId = 0x05AC;
    public const ushort MagicMouse2015ProductId = 0x0269;
    public const ushort MagicTrackpad2ProductId = 0x0265;
    public const ushort MagicKeyboardProductId = 0x0267;

    private static readonly Regex EncodedBluetoothId = new(
        @"VID&(?<encoded>[0-9A-F]{8})_PID&(?<product>[0-9A-F]{4})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PlainHardwareId = new(
        @"VID_(?<vendor>[0-9A-F]{4})&PID_(?<product>[0-9A-F]{4})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParse(string? devicePathOrHardwareId, out AppleDeviceIdentity identity)
    {
        identity = default!;
        if (string.IsNullOrWhiteSpace(devicePathOrHardwareId))
        {
            return false;
        }

        var encodedMatch = EncodedBluetoothId.Match(devicePathOrHardwareId);
        if (encodedMatch.Success)
        {
            var encoded = encodedMatch.Groups["encoded"].Value;
            // Windows Bluetooth HID paths encode the vendor as a 32-bit value,
            // e.g. VID&0001004C. The low 16 bits are the Apple Bluetooth VID.
            if (!TryHex(encoded[^4..], out ushort vendorId) ||
                !TryHex(encodedMatch.Groups["product"].Value, out ushort productId))
            {
                return false;
            }

            var transport = devicePathOrHardwareId.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase)
                || encoded.StartsWith("0001", StringComparison.OrdinalIgnoreCase)
                || encoded.StartsWith("0002", StringComparison.OrdinalIgnoreCase)
                ? AppleTransport.Bluetooth
                : AppleTransport.Unknown;

            return TryCreate(vendorId, productId, transport, devicePathOrHardwareId, out identity);
        }

        var plainMatch = PlainHardwareId.Match(devicePathOrHardwareId);
        if (!plainMatch.Success ||
            !TryHex(plainMatch.Groups["vendor"].Value, out ushort plainVendor) ||
            !TryHex(plainMatch.Groups["product"].Value, out ushort plainProduct))
        {
            return false;
        }

        var plainTransport = plainVendor == AppleUsbVendorId
            ? AppleTransport.Usb
            : devicePathOrHardwareId.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase)
                ? AppleTransport.Bluetooth
                : AppleTransport.Unknown;

        return TryCreate(plainVendor, plainProduct, plainTransport, devicePathOrHardwareId, out identity);
    }

    private static bool TryCreate(
        ushort vendorId,
        ushort productId,
        AppleTransport transport,
        string sourceId,
        out AppleDeviceIdentity identity)
    {
        identity = default!;

        if (vendorId is not AppleBluetoothVendorId and not AppleUsbVendorId)
        {
            return false;
        }

        var kind = productId switch
        {
            MagicMouse2015ProductId => AppleDeviceKind.MagicMouse2015,
            MagicTrackpad2ProductId => AppleDeviceKind.MagicTrackpad2,
            MagicKeyboardProductId => AppleDeviceKind.MagicKeyboard,
            _ => AppleDeviceKind.Unknown
        };

        if (kind == AppleDeviceKind.Unknown)
        {
            return false;
        }

        if (transport == AppleTransport.Unknown && vendorId == AppleBluetoothVendorId)
        {
            transport = AppleTransport.Bluetooth;
        }

        identity = new AppleDeviceIdentity(vendorId, productId, kind, transport, sourceId);
        return true;
    }

    private static bool TryHex(string value, out ushort result) =>
        ushort.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
}
