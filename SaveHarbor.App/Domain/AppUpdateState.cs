namespace SaveHarbor.App.Domain;

public enum AppUpdateState
{
    // Not installed by the installer (dotnet run, an unzipped folder): updates cannot be applied.
    Disabled,
    Idle,
    Checking,
    Available,
    Downloading,
    Ready,
    Failed
}
