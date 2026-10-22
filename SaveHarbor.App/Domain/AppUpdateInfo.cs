namespace SaveHarbor.App.Domain;

// A newer release on the update feed. SizeBytes is what the download costs: the deltas when there are any.
public sealed record AppUpdateInfo(Version Version, string NotesMarkdown, long SizeBytes, bool IsDelta);
