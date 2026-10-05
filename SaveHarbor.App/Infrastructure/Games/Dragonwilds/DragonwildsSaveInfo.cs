namespace SaveHarbor.App.Infrastructure.Games.Dragonwilds;

public enum DragonwildsFieldVisibility
{
    Shown,
    Private,
    NotDecoded
}

// One entry of the save's metadata table. Only the key name and value size are kept, never the raw value.
public sealed record DragonwildsSaveField(string Key, int SizeBytes, DragonwildsFieldVisibility Visibility);

public sealed record DragonwildsSaveInfo(
    string? WorldName,
    string? CreatedBy,
    int? Difficulty,
    bool? FriendlyFire,
    bool? Crossplay,
    bool? PasswordProtected,
    DateTimeOffset? SavedAtUtc,
    string? GameBuild,
    IReadOnlyDictionary<string, float> RuleScales)
{
    public int? FormatVersion { get; init; }

    public string? WorldGuid { get; init; }

    public string? MapName { get; init; }

    public int? HardcoreState { get; init; }

    public int? SessionPrivacy { get; init; }

    public int? SaveRevision { get; init; }

    public IReadOnlyList<string> BuildHistory { get; init; } = [];

    public IReadOnlyList<DragonwildsSaveField> Fields { get; init; } = [];
}
