namespace SaveHarbor.App.Domain;

// A member of a SaveHarbor Cloud group as the other members see them: the display name and role, never the e-mail.
// UserId is only matched against who is online; it is never shown.
public sealed record CloudGroupMember(string DisplayName, string Role, bool IsYou, DateTimeOffset JoinedAtUtc, string UserId = "")
{
    public bool IsOwner => string.Equals(Role, "owner", StringComparison.OrdinalIgnoreCase);
}
