namespace OwnMagicUtilities.Core;

public enum BatteryAvailability
{
    Available = 0,
    NotDetected,
    Unavailable,
    Unsupported,
    Error
}

/// <summary>
/// A normalized battery result. Capacity and health remain null unless a
/// hardware-specific provider actually supplies those measurements.
/// </summary>
public sealed record BatteryReading(
    BatteryAvailability Availability,
    byte? Percentage = null,
    bool? IsCharging = null,
    bool? IsFullyCharged = null,
    DateTimeOffset? ObservedAtUtc = null,
    string? Detail = null,
    int? HealthPercent = null,
    uint? DesignCapacityMWh = null,
    uint? FullChargeCapacityMWh = null)
{
    public static BatteryReading Available(
        byte percentage,
        bool? isCharging,
        bool? isFullyCharged,
        DateTimeOffset? observedAtUtc = null,
        string? detail = null) =>
        new(
            BatteryAvailability.Available,
            percentage,
            isCharging,
            isFullyCharged,
            observedAtUtc ?? DateTimeOffset.UtcNow,
            detail);

    public static BatteryReading Unavailable(string? detail = null) =>
        new(BatteryAvailability.Unavailable, Detail: detail);

    public static BatteryReading NotDetected(string? detail = null) =>
        new(BatteryAvailability.NotDetected, Detail: detail);

    public static BatteryReading Unsupported(string? detail = null) =>
        new(BatteryAvailability.Unsupported, Detail: detail);

    public static BatteryReading Error(string? detail = null) =>
        new(BatteryAvailability.Error, Detail: detail);
}
