using System.Diagnostics;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed class WindowsProcessDetectionService : IProcessDetectionService
{
    private static readonly string[] ProcessNameHints =
    [
        "Windrose",
        "R5"
    ];

    public bool IsWindroseRunning()
    {
        return Process.GetProcesses()
            .Any(process =>
            {
                try
                {
                    return ProcessNameHints.Any(hint =>
                        process.ProcessName.Contains(hint, StringComparison.OrdinalIgnoreCase));
                }
                catch
                {
                    return false;
                }
            });
    }
}
