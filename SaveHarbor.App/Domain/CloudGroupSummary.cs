namespace SaveHarbor.App.Domain;

// A SaveHarbor Cloud group this account belongs to. MyRole is "owner" or "member".
public sealed record CloudGroupSummary(string Id, string Name, string MyRole, int MemberCount, DateTimeOffset CreatedAtUtc)
{
    public bool IsOwner => string.Equals(MyRole, "owner", StringComparison.OrdinalIgnoreCase);
}
