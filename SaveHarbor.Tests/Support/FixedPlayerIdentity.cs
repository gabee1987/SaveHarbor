using SaveHarbor.App.Services;

namespace SaveHarbor.Tests.Support;

public sealed class FixedPlayerIdentity : IPlayerIdentity
{
    public string DisplayName => "TEST_USER";
}
