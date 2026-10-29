namespace SaveHarbor.App.Infrastructure;

// Which backend and data folder the app uses. SAVEHARBOR_ENVIRONMENT=Development runs against the local Supabase stack
// (appsettings.Development.json plus SAVEHARBOR__... environment variables) with its own app data folder, so sign-in,
// sync state and backups never mix with the real ones. Unset or any other value is Production: a published build
// reads only appsettings.json.
public static class AppEnvironment
{
    public const string VariableName = "SAVEHARBOR_ENVIRONMENT";
    public const string Production = "Production";
    public const string Development = "Development";

    public static string Name { get; } = Resolve(Environment.GetEnvironmentVariable(VariableName));

    public static bool IsDevelopment => Name == Development;

    public static string Resolve(string? value) =>
        string.Equals(value?.Trim(), Development, StringComparison.OrdinalIgnoreCase) ? Development : Production;
}
