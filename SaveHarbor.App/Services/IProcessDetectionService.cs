namespace SaveHarbor.App.Services;

public interface IProcessDetectionService
{
    bool IsGameRunning(IGameDefinition game);
}
