# Findings outside the task scope

One line per defect found while implementing the cloud-v2 plans. Not fixed as part of a task.

- 2026-10-20 (CP1): `.gitignore` line `Backup*/` (Visual Studio upgrade template) also ignores `SaveHarbor.Tests/Backup/`, so `BackupSafetyTests.cs`, `DirectoryBackupCompatibilityTests.cs`, `FileSetBackupTests.cs` and `WorldRemovalTests.cs` have never been committed. Fix: add `!SaveHarbor.Tests/Backup/` after that line, then `git add SaveHarbor.Tests/Backup`.
- 2026-10-20 (CP2): `app_meta.min_supported_app_version` is seeded as `2.3.0` by the migration, while the handoff keeps the csproj `<Version>` at 2.2.2 on the feature branch, so every feature-branch build is told "Update required" by SaveHarbor Cloud (local stack and hosted project). Until the owner bumps the version: `update public.app_meta set value = '2.2.2' where key = 'min_supported_app_version';` in the local stack (and hosted, if testing there).
- 2026-10-20 (CP2): Dragonwilds world ids come from save file names (`FileNameSanitizer`) and may contain characters outside the database rule `^[A-Za-z0-9][A-Za-z0-9 _.()-]{0,127}$` (for example accented letters or apostrophes). Such worlds cannot be uploaded to SaveHarbor Cloud; the provider refuses them with a clear message before any request. A mapping from local id to a cloud-safe id would be a schema decision.
