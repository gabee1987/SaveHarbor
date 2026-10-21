namespace SaveHarbor.App.Domain;

// A member of a SaveHarbor Cloud group as the other members see them: the display name and role, never the e-mail.
public sealed record CloudGroupMember(string DisplayName, string Role, bool IsYou, DateTimeOffset JoinedAtUtc)
{
    public bool IsOwner => string.Equals(Role, "owner", StringComparison.OrdinalIgnoreCase);
}
