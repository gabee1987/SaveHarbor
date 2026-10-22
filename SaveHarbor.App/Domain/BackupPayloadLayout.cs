namespace SaveHarbor.App.Domain;

public static class BackupPayloadLayout
{
    // Subfolder of a backup's world payload holding files the game keeps outside the world folder (see
    // IGameSaveAdapter.StageGameFiles). It is never copied into the world folder itself.
    public const string GameFilesFolderName = ".saveharbor-game-files";
}
