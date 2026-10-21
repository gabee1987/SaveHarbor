# Changelog

All notable changes to SaveHarbor. The format follows Keep a Changelog; versions follow Semantic Versioning.
The section of a version is also the release note shown in the app and on the GitHub Release
(`build/Get-ReleaseNotes.ps1`), so keep it to headings, bullets and **bold**: links are shown as plain text.

## [Unreleased]

## [2.3.0]

### Install this version once by hand

SaveHarbor now updates itself, but the old zip cannot do that yet. One time only:

1. Download **SaveHarborApp-win-Setup.exe** from this release and run it. If Windows shows "Windows protected your PC", choose **More info → Run anyway**.
2. Start SaveHarbor from the Start menu or the new desktop shortcut.
3. Delete the old SaveHarbor folder you unzipped earlier.

Your worlds, backups and settings stay where they are, in `%LOCALAPPDATA%\SaveHarbor`. Shortcuts that start a game with `--game` need the new path: `%LOCALAPPDATA%\SaveHarborApp\current\SaveHarbor.App.exe`.

### Added

- **SaveHarbor Cloud:** share worlds without a Google account. Sign in with a code sent to your e-mail; no password.
- **Groups:** create a group and invite friends with an invite code.
- **Safer play sessions:** only one player can host a world at a time, and the lock now renews itself while you play and expires on its own if a PC crashes.
- **Automatic updates:** SaveHarbor checks for new versions, downloads them in the background and asks before restarting. It never restarts while your game is running.
- **Installer:** a normal Windows installer with a Start-menu entry and a desktop shortcut. No admin rights needed.
- The version is shown in the footer and in Settings, with **Check for updates**.

### Changed

- Settings shows how much cloud storage your group uses and can clean up files left over from interrupted uploads.
- An app that is too old for your group's cloud now says **Update required** instead of writing files it does not understand.
