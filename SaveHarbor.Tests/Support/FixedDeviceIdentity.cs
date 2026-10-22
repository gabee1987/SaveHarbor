using SaveHarbor.App.Services;

namespace SaveHarbor.Tests.Support;

public sealed class FixedDeviceIdentity(string deviceId = FixedDeviceIdentity.TestDeviceId) : IDeviceIdentity
{
    public const string TestDeviceId = "00000000-0000-4000-8000-000000012345";

    public string DeviceId => deviceId;

    public string DeviceLabel => "TEST_DEVICE";
}
