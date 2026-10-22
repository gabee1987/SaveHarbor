namespace SaveHarbor.App.Domain;

public sealed class CloudSessionLock
{
    public int SchemaVersion { get; set; } = 2;
    public string LockId { get; set; } = string.Empty;
    public string WorldId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    // Empty in locks written before version 2.3; those are matched by MachineName.
    public string DeviceId { get; set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset LastHeartbeatAtUtc { get; set; }
    public int BasedOnVersionNumber { get; set; }
    public string BasedOnVersionId { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string Status { get; set; } = "Playing";

    public bool IsHeldBy(string deviceId, string machineName) =>
        !string.IsNullOrEmpty(DeviceId) && !string.IsNullOrEmpty(deviceId)
            ? string.Equals(DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)
            : string.Equals(MachineName, machineName, StringComparison.OrdinalIgnoreCase);

    public bool IsLiveAt(DateTimeOffset now) => now <= ExpiresAtUtc;
}
