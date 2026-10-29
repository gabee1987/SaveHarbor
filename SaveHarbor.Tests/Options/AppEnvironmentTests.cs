using SaveHarbor.App.Infrastructure;

namespace SaveHarbor.Tests.Options;

public sealed class AppEnvironmentTests
{
    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    [InlineData(" DEVELOPMENT ")]
    public void Resolve_Development_IsDevelopment(string value)
    {
        Assert.Equal(AppEnvironment.Development, AppEnvironment.Resolve(value));
    }

    // Anything that is not clearly Development must keep the published behaviour.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Production")]
    [InlineData("Dev")]
    [InlineData("Staging")]
    public void Resolve_AnythingElse_IsProduction(string? value)
    {
        Assert.Equal(AppEnvironment.Production, AppEnvironment.Resolve(value));
    }
}
