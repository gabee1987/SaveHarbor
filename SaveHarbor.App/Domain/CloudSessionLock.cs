namespace SaveHarbor.App.Domain;

public sealed class CloudSessionLock
{
    public int SchemaVersion { get; set; } = 1;
    public string LockId { get; set; } = string.Empty;
    public string WorldId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset LastHeartbeatAtUtc { get; set; }
    public int BasedOnVersionNumber { get; set; }
    public string BasedOnVersionId { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string Status { get; set; } = "Playing";
}
