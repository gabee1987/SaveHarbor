using System.Diagnostics;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed class WindowsProcessDetectionService : IProcessDetectionService
{
    public bool IsGameRunning(IGameDefinition game)
    {
        return game.ProcessMatch == ProcessMatch.Exact
            ? game.ProcessNames.Any(IsProcessRunningExact)
            : IsAnyProcessNameContaining(game.ProcessNames);
    }

    private static bool IsProcessRunningExact(string processName)
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

    private static bool IsAnyProcessNameContaining(IReadOnlyList<string> hints)
    {
        var processes = Process.GetProcesses();
        try
        {
            return processes.Any(process =>
            {
                try
                {
                    return hints.Any(hint => process.ProcessName.Contains(hint, StringComparison.OrdinalIgnoreCase));
                }
                catch
                {
                    return false;
                }
            });
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
