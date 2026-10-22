using System.Diagnostics;
using System.IO;

namespace SaveHarbor.App.Utilities;

public static class ExplorerLauncher
{
    // For SaveHarbor's own folders, which are created when missing.
    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    // For folders owned by a game: never created by SaveHarbor; the nearest existing parent is opened instead.
    public static void OpenExistingFolder(string path)
    {
        var folder = new DirectoryInfo(path);
        while (folder is { Exists: false })
        {
            folder = folder.Parent;
        }

        if (folder is not null)
        {
            Process.Start(new ProcessStartInfo { FileName = folder.FullName, UseShellExecute = true });
        }
    }

    // Opens the containing folder with the file selected. Falls back to the folder when the file is gone.
    public static void ShowFile(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            OpenExistingFolder(Path.GetDirectoryName(path)!);
            return;
        }

        // Windows paths cannot contain quotes, so quoting the path is enough to keep it one argument.
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Path.GetFullPath(path)}\""));
    }
}
