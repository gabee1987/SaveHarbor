using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface IGameRegistry
{
    IReadOnlyList<IGameDefinition> All { get; }

    IGameDefinition Get(GameId id);
}
