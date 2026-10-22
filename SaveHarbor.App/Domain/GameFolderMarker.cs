namespace SaveHarbor.App.Domain;

public sealed class GameFolderMarker
{
    public const string FileName = "saveharbor-game.json";

    public int SchemaVersion { get; set; } = 1;
    public string Game { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
