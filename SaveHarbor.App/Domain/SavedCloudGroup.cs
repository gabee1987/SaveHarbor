namespace SaveHarbor.App.Domain;

// A SaveHarbor Cloud group from the last group list, kept so Settings can show its name offline.
public sealed class SavedCloudGroup
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
