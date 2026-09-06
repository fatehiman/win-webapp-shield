namespace WebAppShield;

/// <summary>
/// Watches how long the window has been out of sight and asks the form to drop the
/// embedded browser when that time passes the limit.
///
/// "Inactive" only means the window is minimized or hidden in the tray. A window that
/// is open on the desktop is never put to sleep, even if the user does not touch it.
/// </summary>
public sealed class SleepManager : IDisposable
{
    /// <summary>Free memory below this fraction counts as "the machine is under pressure".</summary>
    public const double LowMemoryFraction = 0.20;

    /// <summary>Idle minutes used by "auto" while memory is tight.</summary>
    public const int AutoLowMemoryMinutes = 10;

    /// <summary>Idle minutes used by "auto" while there is plenty of memory.</summary>
    public const int AutoRelaxedMinutes = 120;

    private readonly System.Windows.Forms.Timer _timer;
    private readonly Action _onSleep;
    private readonly string _setting;
    private DateTime? _inactiveSince;

    public bool Enabled { get; }
    public bool IsAsleep { get; private set; }

    public SleepManager(string setting, Action onSleep)
    {
        _setting = (setting ?? "auto").Trim();
        _onSleep = onSleep;

        Enabled = !(_setting.Equals("off", StringComparison.OrdinalIgnoreCase)
                    || _setting.Equals("never", StringComparison.OrdinalIgnoreCase)
                    || AppConfig.ParseMinutes(_setting) == 0);

        _timer = new System.Windows.Forms.Timer { Interval = 30_000 };
        _timer.Tick += (_, _) => Check();
        if (Enabled) _timer.Start();
    }

    /// <summary>Current limit in minutes. Recomputed on every tick when the mode is "auto".</summary>
    public int CurrentLimitMinutes
    {
        get
        {
            if (_setting.Equals("auto", StringComparison.OrdinalIgnoreCase))
                return NativeMethods.GetFreeMemoryFraction() < LowMemoryFraction
                    ? AutoLowMemoryMinutes
                    : AutoRelaxedMinutes;

            return AppConfig.ParseMinutes(_setting) ?? AutoRelaxedMinutes;
        }
    }

    /// <summary>The window went out of sight (minimized or hidden in the tray).</summary>
    public void MarkInactive()
    {
        if (!Enabled) return;
        _inactiveSince ??= DateTime.UtcNow;
    }

    /// <summary>The window is on screen again.</summary>
    public void MarkActive()
    {
        _inactiveSince = null;
        IsAsleep = false;
    }

    /// <summary>Called by the form after it really did tear the browser down.</summary>
    public void MarkAsleep()
    {
        IsAsleep = true;
        _inactiveSince = null;
    }

    private void Check()
    {
        if (!Enabled || IsAsleep || _inactiveSince is null) return;

        int limit = CurrentLimitMinutes;
        if (limit <= 0) return;

        if (DateTime.UtcNow - _inactiveSince.Value >= TimeSpan.FromMinutes(limit))
            _onSleep();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
