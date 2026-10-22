using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface IAppUpdateService
{
    // False under dotnet run or in an unzipped folder; the state is then Disabled.
    bool IsInstalled { get; }

    Version CurrentVersion { get; }

    AppUpdateState State { get; }

    AppUpdateInfo? Available { get; }

    // 0-100 while downloading.
    int DownloadProgress { get; }

    // Why the last check or download failed, in plain words.
    string? LastError { get; }

    DateTimeOffset? LastCheckedAtUtc { get; }

    bool IsApplyOnExitArmed { get; }

    // Raised on any thread.
    event EventHandler? StateChanged;

    Task CheckAsync(bool userInitiated, CancellationToken cancellationToken = default);

    Task DownloadAsync(IProgress<int>? progress, CancellationToken cancellationToken = default);

    // Installs the downloaded update once the app has exited and starts the new version. False while the game runs or
    // when nothing is ready; the caller then closes the app.
    bool ApplyAndRestart();

    // Installs the downloaded update when the app closes, without starting it again.
    void ApplyOnExit();

    void SkipVersion(Version version);
}
