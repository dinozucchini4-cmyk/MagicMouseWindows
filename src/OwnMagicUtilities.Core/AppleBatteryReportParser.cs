namespace OwnMagicUtilities.Core;

/// <summary>
/// Parses the battery input report format observed in the existing Own Magic
/// Utilities reader: report ID 0x90 and percentage in byte 2.
/// Charging state, health, and capacity are left unknown until a source has
/// been verified for those fields.
/// </summary>
public static class AppleBatteryReportParser
{
    public const byte ReportId = 0x90;
    public const int MinimumReportLength = 3;

    public static bool TryParse(ReadOnlySpan<byte> report, out BatteryReading reading)
    {
        if (report.Length < MinimumReportLength)
        {
            reading = BatteryReading.Unsupported(
                $"Battery report is too short: expected at least {MinimumReportLength} bytes.");
            return false;
        }

        if (report[0] != ReportId)
        {
            reading = BatteryReading.Unsupported(
                $"Unexpected battery report ID 0x{report[0]:X2}; expected 0x{ReportId:X2}.");
            return false;
        }

        if (report[2] > 100)
        {
            reading = BatteryReading.Unsupported(
                $"Battery percentage {report[2]} is outside the valid 0..100 range.");
            return false;
        }

        // Byte 1 semantics are not assumed. Only the report ID and percentage
        // field are used; other battery metadata remains unknown.
        reading = BatteryReading.Available(
            percentage: report[2],
            isCharging: null,
            isFullyCharged: null);

        return true;
    }
}
