using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface IActiveGameContext
{
    IGameDefinition Current { get; }

    event EventHandler<IGameDefinition>? ActiveGameChanged;

    void SetActive(GameId game);
}
