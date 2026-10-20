using SaveHarbor.App.Domain;

namespace SaveHarbor.App.ViewModels;

public enum CloudGroupSetupStep
{
    Email,
    Code,
    Group,
    Created
}

public sealed record CloudGroupSetupResult(string GroupId, string GroupName);

// A row of the group list. The member count is unknown (0) for groups remembered from an earlier start.
public sealed record CloudGroupOption(CloudGroupSummary Group)
{
    public string Name => Group.Name;

    public string Detail => Group.MemberCount > 0
        ? $"{Group.MemberCount} member(s) · {(Group.IsOwner ? "you own it" : "member")}"
        : Group.IsOwner ? "You own it" : "Member";
}
