using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface IAppSettingsStore
{
    AppSettings Current { get; }

    void Update(Action<AppSettings> change);
}
