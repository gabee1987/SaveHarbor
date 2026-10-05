using System.IO;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace SaveHarbor.App.Infrastructure;

public sealed partial class GoogleDriveCloudProvider
{
    private async Task<DriveService> GetOrCreateServiceAsync(bool interactive, CancellationToken cancellationToken)
    {
        if (driveService is not null)
        {
            return driveService;
        }

        await connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (driveService is not null)
            {
                return driveService;
            }

            var credential = interactive
                ? await CreateInteractiveCredentialAsync(cancellationToken)
                : await CreateSilentCredentialAsync(cancellationToken);

            driveService = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "SaveHarbor"
            });

            return driveService;
        }
        finally
        {
            connectionLock.Release();
        }
    }

    private async Task<UserCredential> CreateInteractiveCredentialAsync(CancellationToken cancellationToken)
    {
        var secrets = LoadClientSecrets();
        return await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets,
            Scopes,
            "SaveHarbor",
            cancellationToken,
            new FileDataStore(pathProvider.GoogleTokenStorePath, fullPath: true));
    }

    private async Task<UserCredential> CreateSilentCredentialAsync(CancellationToken cancellationToken)
    {
        var secrets = LoadClientSecrets();
        var dataStore = new FileDataStore(pathProvider.GoogleTokenStorePath, fullPath: true);
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = secrets,
            Scopes = Scopes,
            DataStore = dataStore
        });

        var token = await flow.LoadTokenAsync("SaveHarbor", cancellationToken);
        if (token is null)
        {
            throw new InvalidOperationException("No saved Google Drive token exists.");
        }

        if (!HasRequiredScopes(token.Scope))
        {
            throw new InvalidOperationException("Saved Google Drive token uses old permissions. Use Connect again to approve shared-folder sync.");
        }

        var credential = new UserCredential(flow, "SaveHarbor", token);
        if (credential.Token.IsStale && !await credential.RefreshTokenAsync(cancellationToken))
        {
            throw new InvalidOperationException("Saved Google Drive token could not be refreshed.");
        }

        return credential;
    }

    private ClientSecrets LoadClientSecrets()
    {
        var secretsPath = options.ResolveGoogleClientSecretsPath(pathProvider);
        using var stream = GoogleClientSecretsProtector.OpenRead(secretsPath);
        return GoogleClientSecrets.FromStream(stream).Secrets;
    }

    private static bool HasRequiredScopes(string? grantedScopes)
    {
        return !string.IsNullOrWhiteSpace(grantedScopes) &&
               grantedScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                   .Any(scope => string.Equals(scope, DriveService.Scope.Drive, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string?> LoadAccountEmailAsync(DriveService service, CancellationToken cancellationToken)
    {
        try
        {
            var request = service.About.Get();
            request.Fields = "user(emailAddress,displayName)";
            var about = await request.ExecuteAsync(cancellationToken);
            return about.User?.EmailAddress ?? about.User?.DisplayName;
        }
        catch (Exception exception)
        {
            logger.Warning(AppLogKeyword.CloudProvider, "Could not read Google Drive account info: {Message}", exception.Message);
            return null;
        }
    }
}
