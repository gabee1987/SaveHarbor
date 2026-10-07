namespace SaveHarbor.App.Domain;

// A shared folder this PC has used, offered again in the folder setup dialog. Only the folder ID and the folder's
// name in Google Drive are kept.
public sealed class SavedCloudFolder
{
    public string Game { get; set; } = string.Empty;
    public string FolderId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset LastUsedUtc { get; set; }
}
