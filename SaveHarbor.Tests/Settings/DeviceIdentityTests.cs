using SaveHarbor.App.Infrastructure;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Settings;

public sealed class DeviceIdentityTests : IDisposable
{
    private readonly TempDirectory temp = new();
    private readonly TestPathProvider paths;

    public DeviceIdentityTests()
    {
        paths = new TestPathProvider(temp);
    }

    public void Dispose() => temp.Dispose();

    [Fact]
    public void DeviceId_IsGeneratedOnceAndPersisted()
    {
        var first = new DeviceIdentity(new AppSettingsStore(paths)).DeviceId;
        var afterRestart = new DeviceIdentity(new AppSettingsStore(paths)).DeviceId;

        Assert.True(Guid.TryParseExact(first, "D", out _));
        Assert.Equal(first, afterRestart);
    }

    [Fact]
    public void DeviceId_InvalidStoredValue_IsReplaced()
    {
        var settings = new AppSettingsStore(paths);
        settings.Update(current => current.DeviceId = "TEST_NOT_A_GUID");

        var deviceId = new DeviceIdentity(settings).DeviceId;

        Assert.True(Guid.TryParse(deviceId, out _));
        Assert.Equal(deviceId, new AppSettingsStore(paths).Current.DeviceId);
    }

    [Fact]
    public void DeviceLabel_EmptyUsesComputerName()
    {
        var label = new DeviceIdentity(new AppSettingsStore(paths)).DeviceLabel;

        Assert.Equal(PlayerIdentity.Normalize(Environment.MachineName), label);
    }

    [Fact]
    public void DeviceLabel_IsNormalisedLikePlayerNames()
    {
        var settings = new AppSettingsStore(paths);
        settings.Update(current => current.DeviceLabel = "  TEST_\u0007DEVICE_" + new string('X', 40));

        var label = new DeviceIdentity(settings).DeviceLabel;

        Assert.StartsWith("TEST_DEVICE_", label);
        Assert.Equal(PlayerIdentity.MaxLength, label.Length);
    }
}
