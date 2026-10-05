namespace SaveHarbor.App.Domain;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string LastGame { get; set; } = string.Empty;
}
