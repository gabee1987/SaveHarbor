# Findings outside the task scope

One line per defect found while implementing the cloud-v2 plans. Not fixed as part of a task.

- 2026-10-20 (CP1): `.gitignore` line `Backup*/` (Visual Studio upgrade template) also ignores `SaveHarbor.Tests/Backup/`, so `BackupSafetyTests.cs`, `DirectoryBackupCompatibilityTests.cs`, `FileSetBackupTests.cs` and `WorldRemovalTests.cs` have never been committed. Fix: add `!SaveHarbor.Tests/Backup/` after that line, then `git add SaveHarbor.Tests/Backup`.
