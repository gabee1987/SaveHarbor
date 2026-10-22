using System.Diagnostics;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed class WindowsProcessDetectionService : IProcessDetectionService
{
    public bool IsGameRunning(IGameDefinition game) => game.ProcessNames.Any(IsProcessRunning);

    private static bool IsProcessRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}
