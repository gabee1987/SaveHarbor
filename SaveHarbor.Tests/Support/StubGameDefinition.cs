using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.Tests.Support;

public sealed class StubGameDefinition(GameId id, IGameSaveAdapter? saveAdapter = null) : IGameDefinition
{
    public GameId Id => id;

    public string StorageKey => id.ToStorageKey();

    public string DisplayName => id.ToString();

    public string LaunchUri => string.Empty;

    public string ExecutablePath => string.Empty;

    public IReadOnlyList<string> ProcessNames { get; } = [];

    public ProcessMatch ProcessMatch => ProcessMatch.Exact;

    public TimeSpan SaveSettleDelay => TimeSpan.Zero;

    public Uri ThemeDictionary { get; } = new("/Resources/Styles/DarkTheme.xaml", UriKind.Relative);

    public string? PostRestoreHint => null;

    public string PlayHint => string.Empty;

    public IGameSaveAdapter SaveAdapter => saveAdapter ?? throw new NotSupportedException();
}

public sealed class StubGameRegistry(params IGameDefinition[] definitions) : IGameRegistry
{
    public IReadOnlyList<IGameDefinition> All => definitions;

    public IGameDefinition Get(GameId id) => definitions.First(definition => definition.Id == id);
}
