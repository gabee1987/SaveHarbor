# Changelog

All notable changes to SaveHarbor. The format follows Keep a Changelog; versions follow Semantic Versioning.
The section of a version is also the release note shown in the app and on the GitHub Release
(`build/Get-ReleaseNotes.ps1`), so keep it to headings, bullets and **bold**: links are shown as plain text.

## [Unreleased]

## [2.4.0] - 2026-10-29

### Added

- **Enshrouded:** SaveHarbor now supports Enshrouded as its third game. Pick it in the game switch at the top.
- **Your Steam Cloud worlds:** SaveHarbor finds the worlds on the game's **Steam** tab of the Steam account that is signed in, shows each world once with its name and the slot the game shows, and keeps a downloaded world in that folder, so Steam backs it up after every session as well.
- **World rules:** the details panel shows the main settings of an Enshrouded world, and **All world info** shows all of them.
- **The game's own backups:** All world info offers the game's backup copy of a world. SaveHarbor shows how it compares and backs up your current copy before putting it back.
- **Older copy warning:** if an older copy of an Enshrouded world comes back (for example from Steam), SaveHarbor stops before sharing it over your friends' newer progress and asks you first.
- **Same world twice:** SaveHarbor warns when an Enshrouded world sits in more than one slot and tells you which copies to delete in the game.
- **Its own look:** Enshrouded has its own colours, frames and a drifting mist in the background (turn it off with the ambient effects setting).
- The companion web page lists Enshrouded worlds too.
- **Patch notes:** see what changed in every version of SaveHarbor, by month. They open by themselves once after an update, and any time from the footer or Settings › About.

### Changed

- **Removing an Enshrouded world:** delete it inside the game, because Steam Cloud would bring it back otherwise. SaveHarbor removes a world file itself only when Steam does not know it.
- A downloaded Enshrouded world goes into a slot the game has never used, because Steam hides a new file with the name of a world deleted in the game.

### Fixed

- **Sharing when the game closes:** progress was sometimes not shared and the world stayed locked after the game closed. This affected every game.
- World names on the world cards and the note at the top of **All world info** are no longer cut off.

## [2.3.0] - 2026-10-22

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

## [2.2.2] - 2026-10-08

### Changed

- **Windrose:** the game's own world archive is now treated as the save. SaveHarbor backs up the world the game rebuilds from it, and the false "archive differs" warning is gone.
- SaveHarbor looks at the worlds again by itself when the game's save folder changes, and a deleted world is no longer left selected.

### Fixed

- **Windrose and Steam Cloud:** SaveHarbor warns that a removed world comes back unless you delete it in the game, and a placed world archive counts as the newest one.
- Loading spinners keep turning smoothly.

## [2.2.1] - 2026-10-08

### Fixed

- **Windrose world sharing:** every backup carries the game's own world archive and puts it back on restore and download, so a shared world shows up in the game.

## [2.2.0] - 2026-10-07

### Added

- **Windrose save health:** SaveHarbor notices when the game replaced a world, asks before uploading it, and moves leftover restore folders aside.
- Windrose world settings are shown with the world.

### Changed

- Safer Windrose save handling: exact game process names, checks of the island and the save format, and files prepared outside the game's folder.
- One cloud folder can no longer be used for two games.
- Every game uses the same screen layout, each in its own look.

## [2.1.0] - 2026-10-07

### Added

- **Several worlds per game,** on this PC and in the cloud.
- **Remove a world** from this PC, with a backup first.
- Backups are kept per world, and paths can be copied with one click.
- Cloud folder IDs are remembered for later.

### Changed

- A redesigned comparison of this PC's copy and the cloud's.
- Dropdowns in the style of each game, and notices stay longer and can be closed.

## [2.0.0] - 2026-10-05

### Added

- **RuneScape: Dragonwilds:** SaveHarbor supports a second game. Pick it in the game switch at the top.
- **A look for each game,** with the game switch in the header.
- More information about each world save.
- Choose how many backups and cloud versions are kept.

### Changed

- **A new interface,** redesigned from the ground up.
- Backups, data and cloud folders are kept separately for each game.

### Fixed

- This PC's copy and the cloud's are compared correctly again.

## [1.0.2] - 2026-06-29

### Added

- **Back up and share Windrose worlds** through a Google Drive folder, with a backup before anything is replaced.
- **Play sessions:** starting the game marks the world as yours, and closing the game ends the session, so only one friend hosts at a time.
- Import a world save by hand, and download a cloud save to a PC that has no copy yet.
- Progress bars, notices and a log for every action. Your sign-in is stored encrypted.
