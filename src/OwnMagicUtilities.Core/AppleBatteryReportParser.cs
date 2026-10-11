namespace OwnMagicUtilities.Core;

/// <summary>
/// Parses the Apple HID battery input report format observed in the existing
/// Own Magic Utilities reader: report ID 0x90 and percentage in byte 2.
/// This parser performs no device I/O and writes no reports.
/// </summary>
public static class AppleBatteryReportParser
{
    public const byte ReportId = 0x90;
    public const int MinimumReportLength = 3;
    private const byte ChargingFlag = 0x02;
    private const byte FullyChargedFlag = 0x04;

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

        var flags = report[1];
        reading = BatteryReading.Available(
            percentage: report[2],
            isCharging: (flags & ChargingFlag) != 0,
            isFullyCharged: (flags & FullyChargedFlag) != 0);

        // The 0x90 report does not provide health or capacity values; those
        // metrics intentionally remain null (shown as N/A by the UI).
        return true;
    }
}
