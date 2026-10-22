using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Games;

public sealed class GameRegistry(IEnumerable<IGameDefinition> definitions) : IGameRegistry
{
    private readonly IGameDefinition[] _definitions = definitions.ToArray();

    public IReadOnlyList<IGameDefinition> All => _definitions;

    public IGameDefinition Get(GameId id) =>
        _definitions.FirstOrDefault(definition => definition.Id == id)
        ?? throw new InvalidOperationException($"No game definition is registered for {id}.");
}
