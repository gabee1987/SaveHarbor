namespace SaveHarbor.App.Domain;

public enum SaveHealthIssue
{
    // Something other than SaveHarbor swapped the world's files after SaveHarbor last wrote or accepted them.
    WorldReplaced,

    // Folders an interrupted SaveHarbor restore left next to the world, where the game may load them as worlds.
    Leftovers,

    // The game has no archive of its own for the world, so it will not list it (Windrose).
    GameArchiveMissing
}

public sealed record SaveHealthNotice(SaveHealthIssue Issue, string Title, string Detail);
