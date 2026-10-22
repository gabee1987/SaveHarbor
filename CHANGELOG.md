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
- **Shared when the game closes:** after you play with **Play**, SaveHarbor waits until the game has finished saving, uploads your progress and frees the world for your friends. Turn off **Share progress when the game closes** in Settings › Sharing to be asked each time instead.
- **Upload reminder:** progress that is not shared yet stays in a banner with **Upload now** until you upload it.
- **Out of the way while you play:** the window moves to the notification area during a session and comes back when the game closes.
- **Live updates (SaveHarbor Cloud):** a friend's new version, or a friend starting or finishing a session, appears within seconds with a short notice. A dot next to the cloud status shows **Live**; without a connection SaveHarbor checks every minute.
- **Who is online:** the group shows which friends have SaveHarbor open. Turn off **Show me as online** in Settings › Sharing to stay hidden.
- **Companion web page:** see from your phone who is playing and the newest version of each world. Sign in with the same e-mail code. The page only shows; playing, uploading and downloading stay in the app.
- **Continue with Google:** sign in to SaveHarbor Cloud with your Google account, in the app and on the companion page, when the group's cloud has it switched on. The browser opens Google's own sign-in; SaveHarbor never sees your Google password. If you already signed in with the same e-mail address, it is the same account.
- **Join links:** a saveharbor://join link from a friend opens SaveHarbor with the invite code filled in. A second start of SaveHarbor brings the open window forward instead.
- **Shortcuts:** Ctrl+U uploads, Ctrl+R refreshes, Esc closes Settings.
- A new app icon.

### Changed

- Settings shows how much cloud storage your group uses and can clean up files left over from interrupted uploads.
- An app that is too old for your group's cloud now says **Update required** instead of writing files it does not understand.
- **Sharing in Settings:** choosing where to share, signing in, the group with its members and invite code, and your player name are now on one Sharing page; Settings is split into pages.
- **Faster start:** the last cloud status is shown at once, marked with when it was checked, while SaveHarbor checks again in the background. An upload of a world that has not changed since the last version is skipped.
- **Play checks first:** before starting the game, Play says when a newer version is in the cloud or a friend is playing the world.
