using System.IO;

namespace SaveHarbor.App.Infrastructure;

// One SaveHarbor per Windows user. A second start (a shortcut, or an invite link) hands its arguments to the running
// app and exits; the running app comes forward and handles them. Two apps would watch the same game and race for the
// same session lock.
public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\SaveHarbor.App";
    private const string ActivateEventName = @"Local\SaveHarbor.App.Activate";

    private readonly Mutex mutex;
    private readonly EventWaitHandle activate;
    private readonly string argsPath;
    private RegisteredWaitHandle? registration;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle activate, string argsPath)
    {
        this.mutex = mutex;
        this.activate = activate;
        this.argsPath = argsPath;
    }

    // Raised on a thread-pool thread with the second start's arguments.
    public event Action<IReadOnlyList<string>>? Activated;

    // Null when another SaveHarbor already runs for this user.
    public static SingleInstanceGuard? TryAcquire(string appDataRoot)
    {
        var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        var guard = new SingleInstanceGuard(mutex, new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName), ArgsPath(appDataRoot));
        guard.registration = ThreadPool.RegisterWaitForSingleObject(guard.activate, (_, _) => guard.OnActivate(), null, Timeout.Infinite, executeOnlyOnce: false);
        return guard;
    }

    // From the second start: leaves the arguments for the running app and wakes it.
    public static void SignalRunningInstance(string appDataRoot, IReadOnlyList<string> args)
    {
        try
        {
            Directory.CreateDirectory(appDataRoot);
            File.WriteAllLines(ArgsPath(appDataRoot), args.Take(8).Select(argument => argument.Length > 300 ? argument[..300] : argument));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The running app still comes forward, only without the arguments.
        }

        if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var handle))
        {
            using (handle)
            {
                handle.Set();
            }
        }
    }

    public void Dispose()
    {
        registration?.Unregister(null);
        activate.Dispose();
        mutex.ReleaseMutex();
        mutex.Dispose();
    }

    private static string ArgsPath(string appDataRoot) => Path.Combine(appDataRoot, "activate.args");

    private void OnActivate()
    {
        string[] args = [];
        try
        {
            if (File.Exists(argsPath))
            {
                args = File.ReadAllLines(argsPath);
                File.Delete(argsPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            args = [];
        }

        Activated?.Invoke(args);
    }
}
