namespace SaveHarbor.App.Domain;

// The newest shared-file formats this app understands. A manifest or lock written by a newer app is left alone, so an
// old app never rewrites a format it does not know.
public static class CloudSchema
{
    public const int MaxKnownManifestVersion = 2;

    public const int MaxKnownLockVersion = 2;
}
