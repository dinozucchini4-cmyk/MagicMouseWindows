using OwnMagicUtilities.Core;

var tests = new (string Name, Action Run)[]
{
    ("Bluetooth Mouse identity", TestBluetoothMouseIdentity),
    ("Bluetooth Trackpad identity", TestBluetoothTrackpadIdentity),
    ("Bluetooth Keyboard identity", TestBluetoothKeyboardIdentity),
    ("USB Trackpad identity", TestUsbTrackpadIdentity),
    ("reject unrelated vendor", TestRejectUnrelatedVendor),
    ("reject unsupported Mouse v3 PID", TestRejectUnsupportedMousePid),
    ("battery 80 percent report", TestBattery80Percent),
    ("long Windows report buffer", TestLongBatteryReport),
    ("charging state remains unknown", TestChargingStateRemainsUnknown),
    ("reject short battery report", TestShortReport),
    ("reject wrong report ID", TestWrongReportId),
    ("reject invalid percentage", TestInvalidPercentage),
    ("battery metrics remain unknown", TestCapacityMetricsRemainUnknown),
    ("provider router falls through to valid reading", TestProviderRouter),
    ("router rejects invalid available result", TestRouterRejectsInvalidAvailableResult)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS: {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL: {test.Name}: {ex.Message}");
    }
}

Console.WriteLine($"Tests: {tests.Length - failed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;

static void TestBluetoothMouseIdentity()
{
    const string path = @"BTHENUM\{00001124-0000-1000-8000-00805f9b34fb}_VID&0001004c_PID&0269";
    Check(AppleDeviceIdentity.TryParse(path, out var id), "mouse path was not recognized");
    Equal((ushort)0x004C, id.VendorId);
    Equal((ushort)0x0269, id.ProductId);
    Equal(AppleDeviceKind.MagicMouse2015, id.Kind);
    Equal(AppleTransport.Bluetooth, id.Transport);
}

static void TestBluetoothTrackpadIdentity()
{
    const string path = @"\\?\hid#00001124-0000-1000-8000-00805f9b34fb_vid&0001004c_pid&0265&col02#9&2ac49406&4&0001";
    Check(AppleDeviceIdentity.TryParse(path, out var id), "Trackpad HID path was not recognized");
    Equal(AppleDeviceKind.MagicTrackpad2, id.Kind);
    Equal(AppleTransport.Bluetooth, id.Transport);
}

static void TestBluetoothKeyboardIdentity()
{
    const string path = @"BTHENUM\{00001124-0000-1000-8000-00805f9b34fb}_VID&0001004c_PID&0267";
    Check(AppleDeviceIdentity.TryParse(path, out var id), "keyboard path was not recognized");
    Equal(AppleDeviceKind.MagicKeyboard, id.Kind);
    Equal(AppleTransport.Bluetooth, id.Transport);
}

static void TestUsbTrackpadIdentity()
{
    const string hardwareId = @"HID\VID_05AC&PID_0265&COL01";
    Check(AppleDeviceIdentity.TryParse(hardwareId, out var id), "USB Trackpad hardware ID was not recognized");
    Equal((ushort)0x05AC, id.VendorId);
    Equal(AppleDeviceKind.MagicTrackpad2, id.Kind);
    Equal(AppleTransport.Usb, id.Transport);
}

static void TestRejectUnrelatedVendor()
{
    const string hardwareId = @"HID\VID_1234&PID_0269&COL01";
    Check(!AppleDeviceIdentity.TryParse(hardwareId, out _), "unrelated vendor was accepted");
}

static void TestRejectUnsupportedMousePid()
{
    const string path = @"BTHENUM\{00001124-0000-1000-8000-00805f9b34fb}_VID&0001004c_PID&0323";
    Check(!AppleDeviceIdentity.TryParse(path, out _), "Magic Mouse v3 PID was incorrectly classified as the 2015 model");
}

static void TestBattery80Percent()
{
    byte[] report = [0x90, 0x00, 0x50];
    Check(AppleBatteryReportParser.TryParse(report, out var reading), "valid report rejected");
    Equal(BatteryAvailability.Available, reading.Availability);
    Equal((byte)80, reading.Percentage!.Value);
    Check(reading.IsCharging is null, "charging state must not be inferred from unverified flag bits");
    Check(reading.IsFullyCharged is null, "fully charged state must not be inferred from unverified flag bits");
}

static void TestLongBatteryReport()
{
    byte[] report = [0x90, 0x00, 0x50, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    Check(AppleBatteryReportParser.TryParse(report, out var reading), "long valid report rejected");
    Equal((byte)80, reading.Percentage!.Value);
}

static void TestChargingStateRemainsUnknown()
{
    byte[] report = [0x90, 0xFF, 0x2A];
    Check(AppleBatteryReportParser.TryParse(report, out var reading), "valid report rejected");
    Equal((byte)42, reading.Percentage!.Value);
    Check(reading.IsCharging is null, "byte 1 has not been verified as charge flags");
    Check(reading.IsFullyCharged is null, "byte 1 has not been verified as charge flags");
}

static void TestShortReport()
{
    Check(!AppleBatteryReportParser.TryParse([0x90, 0x00], out var reading), "short report accepted");
    Equal(BatteryAvailability.Unsupported, reading.Availability);
}

static void TestWrongReportId()
{
    Check(!AppleBatteryReportParser.TryParse([0x91, 0x00, 0x50], out var reading), "wrong report ID accepted");
    Equal(BatteryAvailability.Unsupported, reading.Availability);
}

static void TestInvalidPercentage()
{
    Check(!AppleBatteryReportParser.TryParse([0x90, 0x00, 0xFF], out var reading), "percentage over 100 accepted");
    Equal(BatteryAvailability.Unsupported, reading.Availability);
}

static void TestCapacityMetricsRemainUnknown()
{
    Check(AppleBatteryReportParser.TryParse([0x90, 0x00, 0x50], out var reading), "valid report rejected");
    Check(reading.HealthPercent is null, "health must remain unknown without a source");
    Check(reading.DesignCapacityMWh is null, "design capacity must remain unknown without a source");
    Check(reading.FullChargeCapacityMWh is null, "full capacity must remain unknown without a source");
}

static void TestProviderRouter()
{
    var device = new AppleDeviceIdentity(0x004C, 0x0267, AppleDeviceKind.MagicKeyboard, AppleTransport.Bluetooth, "test");
    var router = new BatteryProviderRouter(
    [
        new StubProvider("first", true, BatteryReading.Unavailable("interface busy")),
        new StubProvider("second", true, BatteryReading.Available(84, false, false))
    ]);

    var result = router.ReadAsync(device).AsTask().GetAwaiter().GetResult();
    Equal(BatteryAvailability.Available, result.Availability);
    Equal((byte)84, result.Percentage!.Value);
}

static void TestRouterRejectsInvalidAvailableResult()
{
    var device = new AppleDeviceIdentity(0x004C, 0x0267, AppleDeviceKind.MagicKeyboard, AppleTransport.Bluetooth, "test");
    var router = new BatteryProviderRouter(
    [
        new StubProvider("bad-provider", true, BatteryReading.Available(101, false, false))
    ]);

    var result = router.ReadAsync(device).AsTask().GetAwaiter().GetResult();
    Equal(BatteryAvailability.Unsupported, result.Availability);
    Check(result.Percentage is null, "invalid percentage should not be exposed");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"expected {expected}, got {actual}");
    }
}

sealed class StubProvider(string name, bool canRead, BatteryReading result) : IBatteryProvider
{
    public string Name => name;

    public bool CanRead(AppleDeviceIdentity device) => canRead;

    public ValueTask<BatteryReading> ReadAsync(
        AppleDeviceIdentity device,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(result);
}
