using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Games.Windrose;

public sealed class WindroseGameDefinition(GameOptionsProvider optionsProvider, WindroseSaveAdapter saveAdapter) : IGameDefinition
{
    public GameId Id => GameId.Windrose;

    public string StorageKey => Id.ToStorageKey();

    public string DisplayName => "Windrose";

    public string LaunchUri => optionsProvider.Get(Id).LaunchUri;

    public string ExecutablePath => optionsProvider.Get(Id).ExecutablePath;

    public IReadOnlyList<string> ProcessNames { get; } = ["Windrose", "R5"];

    public ProcessMatch ProcessMatch => ProcessMatch.Contains;

    public TimeSpan SaveSettleDelay => TimeSpan.FromSeconds(10);

    public Uri ThemeDictionary { get; } = new("/Resources/Themes/Windrose.Colors.xaml", UriKind.Relative);

    public string? PostRestoreHint => null;

    public string PlayHint => "Starts a cloud session first, then launches Windrose through Steam.";

    public IGameSaveAdapter SaveAdapter => saveAdapter;
}
