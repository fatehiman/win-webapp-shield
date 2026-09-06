using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace WebAppShield;

/// <summary>
/// Allows only one running copy of one exact exe file, and lets a second start ask the
/// running copy to show itself.
///
/// The lock name is built from the full path of the exe, so two copies of the same
/// program placed in two different folders are two different applications and both
/// may run at the same time. Starting the very same file twice is refused.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private Mutex? _mutex;
    private bool _owned;

    private EventWaitHandle? _showEvent;
    private Thread? _listener;
    private volatile bool _stopping;

    /// <summary>Short hash of the exe path. Also used to key the browser profile folder.</summary>
    public string Key { get; }

    private string MutexName => "Local\\WinWebAppShield.Instance." + Key;
    private string ShowEventName => "Local\\WinWebAppShield.Show." + Key;

    public SingleInstance(string exeFullPath)
    {
        Key = HashPath(exeFullPath);
    }

    /// <summary>True when this process took the lock, false when another copy already holds it.</summary>
    public bool TryAcquire()
    {
        // "Local\" scope: per Windows session. Another user logged on at the same time
        // gets their own lock, which is what people expect.
        _mutex = new Mutex(initiallyOwned: false, MutexName, out _);
        try
        {
            _owned = _mutex.WaitOne(TimeSpan.Zero, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            // The previous owner died without releasing. The lock is ours now.
            _owned = true;
        }
        return _owned;
    }

    /// <summary>
    /// Start waiting for a second copy to ask us to show ourselves.
    ///
    /// A named event is used rather than a broadcast window message: once a window is
    /// taken off the taskbar, WinForms gives it an owner window, and Windows skips
    /// owned windows when broadcasting. An event has no such trap and needs no window
    /// at all, so it also works while the app sits in the tray with no window alive.
    /// </summary>
    public void StartListening(Action onShowRequested)
    {
        if (!_owned || _listener is not null) return;

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _listener = new Thread(() =>
        {
            while (!_stopping)
            {
                try
                {
                    if (_showEvent.WaitOne() && !_stopping) onShowRequested();
                }
                catch
                {
                    return;   // handle closed while shutting down
                }
            }
        })
        {
            IsBackground = true,
            Name = "WinWebAppShield.ShowListener"
        };
        _listener.Start();
    }

    /// <summary>Ask the already running copy to bring its window to the front.</summary>
    public bool SignalRunningInstance()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(ShowEventName, out var handle)) return false;
            using (handle) return handle.Set();
        }
        catch
        {
            return false;
        }
    }

    private static string HashPath(string path)
    {
        // Paths are case insensitive on Windows, so normalise before hashing.
        string normalized = Path.GetFullPath(path).TrimEnd('\\').ToLowerInvariant();
        byte[] hash = SHA256.HashData(Encoding.Unicode.GetBytes(normalized));
        return Convert.ToHexString(hash, 0, 12);
    }

    public void Dispose()
    {
        _stopping = true;
        try { _showEvent?.Set(); } catch { /* just waking the listener */ }
        _showEvent?.Dispose();
        _showEvent = null;

        try
        {
            if (_owned) _mutex?.ReleaseMutex();
        }
        catch { /* shutting down anyway */ }
        _mutex?.Dispose();
        _mutex = null;
        _owned = false;
    }
}
