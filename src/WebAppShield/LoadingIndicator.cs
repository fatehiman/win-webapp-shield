namespace WebAppShield;

/// <summary>
/// The little spinner that runs after the window title while a page is loading, the
/// way DOS and Linux tools have always shown "still working".
///
/// The form owns the text: this class only decides which frame is current and calls
/// back. A frame of <c>null</c> means "idle, put the plain title back".
/// </summary>
public sealed class LoadingIndicator : IDisposable
{
    /// <summary>How long a single frame stays on screen.</summary>
    private const int FrameMilliseconds = 120;

    /// <summary>
    /// Safety net. A single page app can change its content without ever reporting
    /// that navigation finished, and a title that spins forever is worse than no
    /// spinner at all, so the animation gives up after this long.
    /// </summary>
    private static readonly TimeSpan MaxSpin = TimeSpan.FromSeconds(60);

    public static readonly Dictionary<string, string[]> Styles =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // The classic. Four ASCII characters, works in every font.
            ["spinner"] = ["|", "/", "-", "\\"],

            // A block bouncing inside brackets, like an old DOS file manager.
            ["bar"] = ["[=   ]", "[ =  ]", "[  = ]", "[   =]", "[  = ]", "[ =  ]"],

            // Quiet and plain.
            ["dots"] = [".", "..", "...", "   "],

            // Smooth, but needs a font with Braille characters (Segoe UI has them).
            ["braille"] = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"],
        };

    private readonly System.Windows.Forms.Timer _timer;
    private readonly string[] _frames;
    private readonly Action<string?> _render;

    private int _index;
    private bool _busy;
    private DateTime _startedUtc;

    /// <summary>False when the config asked for "off"; then nothing ever animates.</summary>
    public bool Enabled { get; }

    public LoadingIndicator(string style, Action<string?> render)
    {
        _render = render;

        string key = (style ?? "spinner").Trim();
        Enabled = !(key.Equals("off", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("none", StringComparison.OrdinalIgnoreCase)
                    || key.Length == 0);

        _frames = Styles.TryGetValue(key, out var frames) ? frames : Styles["spinner"];

        _timer = new System.Windows.Forms.Timer { Interval = FrameMilliseconds };
        _timer.Tick += (_, _) => Advance();
    }

    /// <summary>Tell the indicator whether anything is loading right now.</summary>
    public void SetBusy(bool busy)
    {
        if (!Enabled || busy == _busy) return;
        _busy = busy;

        if (busy)
        {
            _index = 0;
            _startedUtc = DateTime.UtcNow;
            _render(_frames[0]);
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            _render(null);
        }
    }

    private void Advance()
    {
        if (DateTime.UtcNow - _startedUtc > MaxSpin)
        {
            // Still "loading" after a minute. Stop the animation but leave _busy
            // alone, so a later NavigationCompleted is still handled normally.
            _timer.Stop();
            _render(null);
            return;
        }

        _index = (_index + 1) % _frames.Length;
        _render(_frames[_index]);
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }
}
