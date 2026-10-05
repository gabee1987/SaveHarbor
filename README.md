# SaveHarbor

SaveHarbor is a Windows desktop app that backs up, restores and shares world saves for
**Windrose** and **RuneScape: Dragonwilds** through a shared cloud folder (Google Drive).

## Requirements (development)

- Windows 10 or 11
- .NET SDK 10.0.401 or later (pinned in `global.json`)

## Everyday commands

Run these from the repository root (`C:\Coding\SaveHarbor`).

```powershell
# Build
dotnet build SaveHarbor.sln -c Debug

# Run all tests
dotnet test SaveHarbor.sln --no-build

# Start the app (optionally choose the game to open first)
dotnet run --project SaveHarbor.App -c Debug -- --game dragonwilds
dotnet run --project SaveHarbor.App -c Debug -- --game windrose
```

If the build fails because `SaveHarbor.App.exe` is in use, close the running app first.

## Build a zip to share with friends

One command creates a ready-to-run zip. .NET is included, so the recipient does not
need to install anything.

```powershell
cd C:\Coding\SaveHarbor; dotnet publish SaveHarbor.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o artifacts\friend-build; Compress-Archive -Path artifacts\friend-build\* -DestinationPath artifacts\SaveHarbor.zip -Force
```

The result is `artifacts\SaveHarbor.zip` (about 30 MB). The `artifacts` folder is ignored by Git.

| Option | Purpose |
| --- | --- |
| `-c Release` | Optimised production build |
| `-r win-x64 --self-contained true` | Includes .NET, so nothing else has to be installed |
| `PublishSingleFile`, `EnableCompressionInSingleFile` | One compressed `.exe` instead of many DLLs |
| `IncludeNativeLibrariesForSelfExtract` | Bundles native libraries into the `.exe` |
| `-p:DebugType=none` | Leaves out debug files |

The package contains only the app, `appsettings.json`, the UI texts and the encrypted
Google client file. Personal data (sign-in tokens, settings, backups, the chosen shared
folder) is stored per user under `%LOCALAPPDATA%\SaveHarbor` and is never part of the build.

### Before sharing

- **Google sign-in:** while the Google Cloud project's OAuth consent screen is in
  *Testing* mode, only listed accounts can sign in. Add each friend's Google account
  under *Google Cloud Console → OAuth consent screen → Test users*.
- **Shared folder:** give each friend edit access to the shared Google Drive folder.

### What a friend does

1. Unzip `SaveHarbor.zip` anywhere and start `SaveHarbor.App.exe`.
2. If Windows shows "Windows protected your PC", choose **More info → Run anyway**
   (the app is not code-signed).
3. Choose **Connect** and sign in with Google.
4. Choose the shared folder.

## Where data is stored

| What | Location |
| --- | --- |
| App settings | `%LOCALAPPDATA%\SaveHarbor\app-settings.json` |
| Backups | `%LOCALAPPDATA%\SaveHarbor\backups\<game>` |
| Logs | `%LOCALAPPDATA%\SaveHarbor\logs` |
| Dragonwilds saves (game) | `%LOCALAPPDATA%\RSDragonwilds\Saved\SaveGames` |

The backup, log and save folders can also be opened from **Settings** in the app.

## Notes

- The executable is not code-signed. A build distributed beyond a small group of
  friends should be signed and pass a security and compliance review first.
- The Dragonwilds save format is not documented by the game; SaveHarbor reads only
  the metadata it needs and never reads the world password or owner ID.
