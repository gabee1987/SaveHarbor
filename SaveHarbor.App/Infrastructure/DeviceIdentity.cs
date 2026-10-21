using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed class DeviceIdentity(IAppSettingsStore settings) : IDeviceIdentity
{
    private readonly Lock _gate = new();

    public string DeviceId
    {
        get
        {
            if (Guid.TryParse(settings.Current.DeviceId, out _))
            {
                return settings.Current.DeviceId;
            }

            lock (_gate)
            {
                if (!Guid.TryParse(settings.Current.DeviceId, out _))
                {
                    settings.Update(current => current.DeviceId = Guid.NewGuid().ToString("D"));
                }

                return settings.Current.DeviceId;
            }
        }
    }

    public string DeviceLabel => PlayerIdentity.Normalize(settings.Current.DeviceLabel) is { Length: > 0 } label
        ? label
        : PlayerIdentity.Normalize(Environment.MachineName);
}
