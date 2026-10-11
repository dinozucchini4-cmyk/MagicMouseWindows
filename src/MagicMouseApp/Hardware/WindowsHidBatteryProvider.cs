using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using OwnMagicUtilities.Core;

namespace MagicMouseApp.Hardware;

/// <summary>
/// Read-only Windows HID battery provider. It discovers interface paths at
/// runtime and reads only report 0x90 or a descriptor-declared Generic Device
/// Battery Strength feature. It never writes output/feature reports or changes
/// the driver stack.
/// </summary>
public sealed class WindowsHidBatteryProvider : IBatteryProvider
{
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const int HidpStatusSuccess = 0x00110000;
    private const int HidpFeatureReportType = 2;

    private const ushort VendorBatteryUsagePage = 0xFF00;
    private const ushort VendorBatteryUsage = 0x0014;
    private const ushort GenericDeviceBatteryUsagePage = 0x0006;
    private const ushort GenericDeviceBatteryStrengthUsage = 0x0020;

    private static readonly IntPtr InvalidHandleValue = new(-1);

    public string Name => "Windows HID battery reports";

    public bool CanRead(AppleDeviceIdentity device) =>
        device.Kind is AppleDeviceKind.MagicMouse2015
            or AppleDeviceKind.MagicTrackpad2
            or AppleDeviceKind.MagicKeyboard;

    public ValueTask<BatteryReading> ReadAsync(
        AppleDeviceIdentity device,
        CancellationToken cancellationToken) =>
        new(Task.Run(() => ReadCore(device, cancellationToken), cancellationToken));

    private static BatteryReading ReadCore(
        AppleDeviceIdentity target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = new List<(string Path, AppleDeviceIdentity Identity)>();
        foreach (var path in EnumerateHidPaths())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (AppleDeviceIdentity.TryParse(path, out var identity)
                && identity.ProductId == target.ProductId
                && identity.Kind == target.Kind)
            {
                candidates.Add((path, identity));
            }
        }

        if (candidates.Count == 0)
        {
            return BatteryReading.NotDetected($"No HID interface found for {target.Kind} (PID 0x{target.ProductId:X4}).");
        }

        // Prefer Bluetooth over a USB charge-cable interface and prefer COL02,
        // where these devices commonly expose the vendor battery collection.
        var ordered = candidates
            .OrderByDescending(candidate => candidate.Identity.Transport == AppleTransport.Bluetooth)
            .ThenByDescending(candidate => candidate.Path.Contains("COL02", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        BatteryReading? bestFailure = null;
        foreach (var candidate in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reading = TryReadPath(candidate.Path, target.Kind, cancellationToken);

            if (reading.Availability == BatteryAvailability.Available &&
                reading.Percentage is >= 0 and <= 100)
            {
                return reading;
            }

            if (bestFailure is null || FailurePriority(reading) > FailurePriority(bestFailure))
            {
                bestFailure = reading;
            }
        }

        return bestFailure ?? BatteryReading.Unavailable("Matching HID interfaces were found, but no battery reading was returned.");
    }

    private static BatteryReading TryReadPath(
        string path,
        AppleDeviceKind kind,
        CancellationToken cancellationToken)
    {
        using SafeFileHandle handle = CreateFile(
            path,
            0, // Query-only access; avoids requesting read access on pointer-owned collections.
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            return BatteryReading.Unavailable($"HID open failed (Win32 {Marshal.GetLastWin32Error()}).");
        }

        if (!HidD_GetPreparsedData(handle, out var preparsedData))
        {
            return BatteryReading.Unavailable($"HID descriptor access failed (Win32 {Marshal.GetLastWin32Error()}).");
        }

        try
        {
            var caps = new HidpCaps { Reserved = new ushort[17] };
            if (HidP_GetCaps(preparsedData, ref caps) != HidpStatusSuccess)
            {
                return BatteryReading.Unsupported("Windows could not parse this HID collection's capabilities.");
            }

            // First path: Apple's vendor-defined battery input collection, where
            // report 0x90 contains [report ID, flags, percentage, ...].
            if (caps.UsagePage == VendorBatteryUsagePage
                && caps.Usage == VendorBatteryUsage
                && caps.InputReportByteLength >= AppleBatteryReportParser.MinimumReportLength)
            {
                var reportLength = Math.Max((int)caps.InputReportByteLength, 64);
                BatteryReading? lastFailure = null;

                for (var attempt = 0; attempt < 3; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var report = new byte[reportLength];
                    report[0] = AppleBatteryReportParser.ReportId;

                    if (HidD_GetInputReport(handle, report, report.Length))
                    {
                        if (AppleBatteryReportParser.TryParse(report, out var reading))
                        {
                            return reading with { Detail = $"Read from {kind} HID input report 0x90." };
                        }

                        lastFailure = reading;
                        break;
                    }

                    var error = Marshal.GetLastWin32Error();
                    lastFailure = BatteryReading.Unavailable(
                        $"HID input-report query failed (Win32 {error}).");

                    if (attempt < 2)
                    {
                        Thread.Sleep(50);
                    }
                }

                return lastFailure ?? BatteryReading.Unavailable("No response to HID input report 0x90.");
            }

            // Second path: descriptor-declared Generic Device / Battery Strength
            // feature report (normally 0x47 on supported legacy Apple interfaces).
            if (TryFindBatteryFeature(preparsedData, caps, out var reportId)
                && caps.FeatureReportByteLength >= 2)
            {
                var report = new byte[Math.Max((int)caps.FeatureReportByteLength, 2)];
                report[0] = reportId;

                if (!HidD_GetFeature(handle, report, report.Length))
                {
                    return BatteryReading.Unavailable(
                        $"Descriptor-declared battery feature query failed (Win32 {Marshal.GetLastWin32Error()}).");
                }

                if (report[0] != reportId || report[1] > 100)
                {
                    return BatteryReading.Unsupported("The battery feature response did not contain a valid percentage.");
                }

                return BatteryReading.Available(
                    percentage: report[1],
                    isCharging: null,
                    isFullyCharged: null,
                    detail: $"Read from descriptor-declared battery feature 0x{reportId:X2}.");
            }

            return BatteryReading.Unsupported("This HID collection does not expose a recognized battery report.");
        }
        finally
        {
            HidD_FreePreparsedData(preparsedData);
        }
    }

    private static bool TryFindBatteryFeature(
        IntPtr preparsedData,
        HidpCaps caps,
        out byte reportId)
    {
        reportId = 0;
        if (caps.FeatureReportByteLength < 2 || caps.NumberFeatureValueCaps == 0)
        {
            return false;
        }

        var valueCaps = new HidpValueCaps[caps.NumberFeatureValueCaps];
        var valueCapsLength = caps.NumberFeatureValueCaps;

        if (HidP_GetValueCaps(
                HidpFeatureReportType,
                valueCaps,
                ref valueCapsLength,
                preparsedData) != HidpStatusSuccess)
        {
            return false;
        }

        var count = Math.Min(valueCapsLength, (ushort)valueCaps.Length);
        for (var index = 0; index < count; index++)
        {
            var cap = valueCaps[index];
            if (!cap.IsRange
                && cap.UsagePage == GenericDeviceBatteryUsagePage
                && cap.Usage == GenericDeviceBatteryStrengthUsage)
            {
                reportId = cap.ReportID;
                return true;
            }
        }

        return false;
    }

    private static int FailurePriority(BatteryReading reading) => reading.Availability switch
    {
        BatteryAvailability.Error => 4,
        BatteryAvailability.Unsupported => 3,
        BatteryAvailability.Unavailable => 2,
        BatteryAvailability.NotDetected => 1,
        _ => 0
    };

    private static List<string> EnumerateHidPaths()
    {
        HidD_GetHidGuid(out var hidGuid);
        var deviceInfoSet = SetupDiGetClassDevs(
            ref hidGuid,
            IntPtr.Zero,
            IntPtr.Zero,
            DigcfPresent | DigcfDeviceInterface);

        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == InvalidHandleValue)
        {
            return [];
        }

        var paths = new List<string>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var interfaceData = new SpDeviceInterfaceData
                {
                    Size = (uint)Marshal.SizeOf<SpDeviceInterfaceData>()
                };

                if (!SetupDiEnumDeviceInterfaces(
                        deviceInfoSet,
                        IntPtr.Zero,
                        ref hidGuid,
                        index,
                        ref interfaceData))
                {
                    break;
                }

                _ = SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet,
                    ref interfaceData,
                    IntPtr.Zero,
                    0,
                    out var requiredSize,
                    IntPtr.Zero);

                if (requiredSize < 6 || requiredSize > 64 * 1024)
                {
                    continue;
                }

                var detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
                try
                {
                    // Windows requires cbSize 8 on 64-bit and 6 on 32-bit systems.
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);

                    if (SetupDiGetDeviceInterfaceDetail(
                            deviceInfoSet,
                            ref interfaceData,
                            detailBuffer,
                            requiredSize,
                            out _,
                            IntPtr.Zero))
                    {
                        var path = Marshal.PtrToStringUni(IntPtr.Add(detailBuffer, sizeof(uint)));
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            paths.Add(path);
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuffer);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return paths;
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetInputReport(
        SafeFileHandle hidDeviceObject,
        byte[] reportBuffer,
        int reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetFeature(
        SafeFileHandle hidDeviceObject,
        byte[] reportBuffer,
        int reportBufferLength);

    [DllImport("hid.dll")]
    private static extern bool HidD_GetPreparsedData(
        SafeFileHandle hidDeviceObject,
        out IntPtr preparsedData);

    [DllImport("hid.dll")]
    private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(
        IntPtr preparsedData,
        ref HidpCaps capabilities);

    [DllImport("hid.dll")]
    private static extern int HidP_GetValueCaps(
        int reportType,
        [In, Out] HidpValueCaps[] valueCaps,
        ref ushort valueCapsLength,
        IntPtr preparsedData);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        IntPtr enumerator,
        IntPtr parentWindow,
        uint flags);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiEnumDeviceInterfaces", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", EntryPoint = "SetupDiDestroyDeviceInfoList", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public uint Size;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct HidpValueCaps
    {
        public ushort UsagePage;
        public byte ReportID;
        [MarshalAs(UnmanagedType.U1)] public bool IsAlias;
        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;
        [MarshalAs(UnmanagedType.U1)] public bool IsRange;
        [MarshalAs(UnmanagedType.U1)] public bool IsStringRange;
        [MarshalAs(UnmanagedType.U1)] public bool IsDesignatorRange;
        [MarshalAs(UnmanagedType.U1)] public bool IsAbsolute;
        [MarshalAs(UnmanagedType.U1)] public bool HasNull;
        public byte Reserved;
        public ushort BitSize;
        public ushort ReportCount;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public ushort Reserved4;
        public ushort Reserved5;
        public uint UnitsExp;
        public uint Units;
        public int LogicalMin;
        public int LogicalMax;
        public int PhysicalMin;
        public int PhysicalMax;
        public ushort Usage;
        public ushort UsageMax;
        public ushort StringMin;
        public ushort StringMax;
        public ushort DesignatorMin;
        public ushort DesignatorMax;
        public ushort DataIndexMin;
        public ushort DataIndexMax;
    }
}
