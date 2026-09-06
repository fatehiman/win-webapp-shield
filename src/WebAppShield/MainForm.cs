using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WebAppShield;

public sealed class MainForm : Form
{
    // The Form base constructor already reads CreateParams, before any field of this
    // class has been assigned, so this one has to be readable while _cfg is still null.
    private readonly AppConfig? _cfgOrNull;
    private AppConfig _cfg => _cfgOrNull!;
    private readonly SessionStore _session;
    private readonly SingleInstance _instance;
    private readonly string _userDataFolder;
    private readonly string? _browserFolder;

    /// <summary>
    /// Stable target for calls that arrive from another thread. Taking the window off
    /// the taskbar makes WinForms destroy the form's own handle until it is shown
    /// again, so the form itself cannot be used to marshal onto the UI thread.
    /// This little control keeps a handle for the whole life of the app.
    /// </summary>
    private readonly Control _uiMarshal = new();

    private WebView2? _web;
    private Label? _status;
    private NotifyIcon? _tray;
    private SleepManager _sleep = null!;
    private LoadingIndicator _loading = null!;

    /// <summary>Title on its own. Everything else is appended to this.</summary>
    private string _baseTitle = "";

    /// <summary>Current spinner frame, or null when nothing is loading.</summary>
    private string? _loadingFrame;

    /// <summary>"1100x820" while the window is being resized, otherwise null.</summary>
    private string? _resizeSize;

    /// <summary>Clears the size from the title once the resizing stops.</summary>
    private System.Windows.Forms.Timer? _resizeHideTimer;

    /// <summary>Last size reported in the title, so a restore does not re-announce it.</summary>
    private Size? _lastReportedSize;

    // Two separate reasons to show the spinner: starting the browser up, and the
    // page itself navigating. The spinner runs while either is true.
    private bool _startingBrowser;
    private bool _navigating;

    /// <summary>
    /// False until the first page of this browser session has something to show. Until
    /// then the animated placeholder stays in front, because a freshly created WebView2
    /// paints plain white and that looks like a broken app rather than a busy one.
    /// </summary>
    private bool _pageRevealed;
    private System.Windows.Forms.Timer? _revealFallback;

    private string _currentUrl;
    private string? _startHost;

    private bool _initialShowHandled;
    private readonly bool _startHidden;
    private bool _hiddenInTray;
    private bool _exiting;
    private bool _webBusy;
    private FormWindowState _restoreState = FormWindowState.Normal;

    public MainForm(AppConfig cfg, SessionStore session, SingleInstance instance,
                    string userDataFolder, string? browserFolder)
    {
        _cfgOrNull = cfg;
        _session = session;
        _instance = instance;
        _userDataFolder = userDataFolder;
        _browserFolder = string.IsNullOrWhiteSpace(browserFolder) ? null : browserFolder;

        _currentUrl = PickStartUrl();
        // Remember it now: the user may close the window before the browser has even
        // finished starting, and the session file should still know where we were.
        RememberUrl(_currentUrl);
        _startHidden = cfg.WindowState.Trim().Equals("tray", StringComparison.OrdinalIgnoreCase);

        _baseTitle = cfg.EffectiveTitle;
        _loading = new LoadingIndicator(cfg.LoadingIndicator, RenderLoadingFrame);

        BuildWindow();
        BuildTray();

        _sleep = new SleepManager(_cfg.SleepAfter, SleepNow);

        _ = _uiMarshal.Handle;   // force the handle now, on the UI thread

        // A second start of the same exe with single-instance-action "focus" pokes a
        // named event; the listener thread calls this back.
        _instance.StartListening(OnShowRequestedFromOtherInstance);
    }

    /// <summary>Called on a background thread when another copy asked us to show up.</summary>
    private void OnShowRequestedFromOtherInstance()
    {
        try
        {
            if (_uiMarshal.IsDisposed || !_uiMarshal.IsHandleCreated) return;
            _uiMarshal.BeginInvoke(ShowFromTray);
        }
        catch (ObjectDisposedException)
        {
            // The window went away between the check and the call. Nothing to show.
        }
        catch (InvalidOperationException)
        {
            // No handle any more, same story.
        }
    }

    // ------------------------------------------------------------------ setup

    private string PickStartUrl()
    {
        // After a sleep or a restart, go back to the page the user was on.
        if (!string.IsNullOrWhiteSpace(_session.LastUrl) &&
            Uri.TryCreate(_session.LastUrl, UriKind.Absolute, out var last) &&
            (last.Scheme == Uri.UriSchemeHttp || last.Scheme == Uri.UriSchemeHttps))
        {
            return _session.LastUrl;
        }
        return _cfg.Url;
    }

    private void BuildWindow()
    {
        AutoScaleMode = AutoScaleMode.None;   // width/height in the conf file are real pixels
        Text = _baseTitle;
        BackColor = Color.White;
        KeyPreview = false;
        DoubleBuffered = true;
        TopMost = _cfg.AlwaysOnTop;

        var icon = LoadIcon();
        if (icon is not null) Icon = icon;

        if (!_cfg.HasTitleBar)
        {
            // "" -> no caption at all. Resize borders are handled in WndProc.
            FormBorderStyle = FormBorderStyle.None;
            ControlBox = false;
        }
        else
        {
            FormBorderStyle = _cfg.Resizable ? FormBorderStyle.Sizable : FormBorderStyle.FixedSingle;
            ControlBox = true;
            MinimizeBox = _cfg.ShowMinimize;
            MaximizeBox = _cfg.ShowMaximize;
        }

        MinimumSize = new Size(200, 150);
        ApplyStartBounds();
        ApplyStartState();

        _status = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif, 10f),
            ForeColor = Color.FromArgb(110, 110, 110),
            BackColor = Color.White,
            Text = "Loading..."
        };
        Controls.Add(_status);

        Shown += (_, _) =>
        {
            // Take the startup size as already known, so opening the window does not
            // announce a size the user never asked about.
            _lastReportedSize = Bounds.Size;
            EnsureWebView();
        };
        ResizeEnd += (_, _) => { RememberBounds(); SaveSession(); };
    }

    private Icon? LoadIcon()
    {
        if (string.IsNullOrWhiteSpace(_cfg.Icon)) return null;
        try
        {
            string path = Path.IsPathRooted(_cfg.Icon)
                ? _cfg.Icon
                : Path.Combine(Program.ExeDir, _cfg.Icon);
            return File.Exists(path) ? new Icon(path) : null;
        }
        catch
        {
            return null;   // a bad icon must never stop the app
        }
    }

    private void ApplyStartBounds()
    {
        var screen = MonitorHelper.Pick(_cfg.Monitor);
        var size = new Size(_cfg.Width, _cfg.Height);
        string position = (_cfg.Position ?? "center").Trim();

        if (position.Equals("last", StringComparison.OrdinalIgnoreCase))
        {
            var w = _session.Window;
            if (w.X.HasValue && w.Y.HasValue)
            {
                if (_cfg.RememberSize && w.Width is > 0 && w.Height is > 0)
                    size = new Size(w.Width.Value, w.Height.Value);

                var saved = new Rectangle(w.X.Value, w.Y.Value, size.Width, size.Height);
                StartPosition = FormStartPosition.Manual;
                Bounds = MonitorHelper.IsVisible(saved) ? MonitorHelper.Clamp(saved)
                                                        : MonitorHelper.CenterOn(screen, size);
                return;
            }
        }
        else if (AppConfig.ParsePoint(position) is { } point)
        {
            // x,y is measured from the top left of the chosen monitor's working area.
            var area = screen.WorkingArea;
            StartPosition = FormStartPosition.Manual;
            Bounds = MonitorHelper.Clamp(new Rectangle(area.X + point.X, area.Y + point.Y, size.Width, size.Height));
            return;
        }

        StartPosition = FormStartPosition.Manual;
        Bounds = MonitorHelper.CenterOn(screen, size);
    }

    private void ApplyStartState()
    {
        string state = (_cfg.WindowState ?? "normal").Trim().ToLowerInvariant();
        switch (state)
        {
            case "max":
                _restoreState = FormWindowState.Maximized;
                WindowState = FormWindowState.Maximized;
                break;
            case "min":
                WindowState = FormWindowState.Minimized;
                break;
            case "tray":
                _hiddenInTray = true;
                break;
            default:
                // "last" position may also restore a maximized window
                if ((_cfg.Position ?? "").Trim().Equals("last", StringComparison.OrdinalIgnoreCase) &&
                    _session.Window.State.Equals("max", StringComparison.OrdinalIgnoreCase))
                {
                    _restoreState = FormWindowState.Maximized;
                    WindowState = FormWindowState.Maximized;
                }
                break;
        }
    }

    private void BuildTray()
    {
        if (!_cfg.TrayEnabled) return;

        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("&Open", null, (_, _) => ShowFromTray()) { Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold) };
        menu.Items.Add(open);

        if (_cfg.TrayOpenInBrowser)
            menu.Items.Add(new ToolStripMenuItem("Open in &browser", null, (_, _) => OpenInBrowser()));

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("E&xit", null, (_, _) => ExitApp()));

        _tray = new NotifyIcon
        {
            Icon = Icon ?? SystemIcons.Application,
            Text = Truncate(_cfg.EffectiveTitle, 63),
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();
        _tray.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>
    /// The only place the window title is built, so the spinner and the resize size
    /// cannot overwrite each other. The size wins while it is showing: it is the
    /// shorter lived of the two and it is what the user is looking at.
    /// </summary>
    private void UpdateTitle()
    {
        if (_resizeSize is not null) Text = _baseTitle + " - " + _resizeSize;
        else if (_loadingFrame is not null) Text = _baseTitle + "  " + _loadingFrame;
        else Text = _baseTitle;
    }

    /// <summary>
    /// Called by the indicator for every frame. A null frame means "back to idle".
    /// </summary>
    private void RenderLoadingFrame(string? frame)
    {
        _loadingFrame = frame;
        UpdateTitle();

        // The placeholder shown before the browser control exists gets one too.
        if (_status is { Visible: true })
            _status.Text = frame is null ? "Loading..." : "Loading  " + frame;

        // The tray tooltip only says whether it is busy: rewriting it 8 times a
        // second would make Windows rebuild the tooltip for nothing.
        if (_tray is not null)
        {
            string tip = frame is null ? _baseTitle : _baseTitle + " - loading...";
            tip = Truncate(tip, 63);
            if (_tray.Text != tip) _tray.Text = tip;
        }
    }

    private void UpdateLoadingState() => _loading.SetBusy(_startingBrowser || _navigating);

    // ------------------------------------------------------- browser lifetime

    private async void EnsureWebView()
    {
        if (_web is not null || _webBusy) return;
        _webBusy = true;
        _startingBrowser = true;
        UpdateLoadingState();

        WebView2? view = null;
        try
        {
            if (_status is not null) { _status.Text = "Loading..."; _status.Visible = true; }

            view = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Color.White,
                CreationProperties = new CoreWebView2CreationProperties
                {
                    UserDataFolder = _userDataFolder,
                    BrowserExecutableFolder = _browserFolder,
                    AdditionalBrowserArguments = _cfg.WebView2Args
                }
            };
            Controls.Add(view);
            // Deliberately NOT brought to front yet: the placeholder stays visible
            // until the page has content. RevealPage() puts the browser on top.
            _pageRevealed = false;
            _status?.BringToFront();

            await view.EnsureCoreWebView2Async();

            var core = view.CoreWebView2;
            var s = core.Settings;
            s.AreDefaultContextMenusEnabled = _cfg.ContextMenu;
            s.AreDevToolsEnabled = _cfg.DevTools;
            s.IsStatusBarEnabled = true;
            s.AreBrowserAcceleratorKeysEnabled = true;
            if (!string.IsNullOrWhiteSpace(_cfg.UserAgent)) s.UserAgent = _cfg.UserAgent;

            core.NewWindowRequested += OnNewWindowRequested;
            core.NavigationStarting += OnNavigationStarting;
            core.NavigationCompleted += OnNavigationCompleted;
            core.DOMContentLoaded += (_, _) => RevealPage();
            core.SourceChanged += (_, _) => RememberUrl(core.Source);
            core.DocumentTitleChanged += (_, _) => { /* the conf title wins, nothing to do */ };
            core.ProcessFailed += OnProcessFailed;

            view.ZoomFactor = _cfg.Zoom;

            _web = view;

            // Safety net: if a page never reports that it loaded, show it anyway
            // rather than leaving the placeholder on top for ever.
            _revealFallback?.Dispose();
            _revealFallback = new System.Windows.Forms.Timer { Interval = 15_000 };
            _revealFallback.Tick += (_, _) => RevealPage();
            _revealFallback.Start();

            _startHost = TryHost(_cfg.Url);

            // Record it before navigating. SourceChanged only fires once the browser
            // actually moves, and the user may close the window before that.
            RememberUrl(_currentUrl);

            // Hand the spinner over from "starting up" to "loading the page", with no
            // gap in between, so the title never flickers back to idle.
            _navigating = true;
            _startingBrowser = false;
            UpdateLoadingState();

            core.Navigate(_currentUrl);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowFatal("The Microsoft Edge WebView2 Runtime is no longer available.\r\n\r\n" +
                      "Install it from " + WebView2MissingForm.DownloadUrl);
        }
        catch (Exception ex)
        {
            ShowFatal("The embedded browser could not be started.\r\n\r\n" + ex.Message);
        }
        finally
        {
            _webBusy = false;
        }
    }

    /// <summary>Put the browser in front of the placeholder. Safe to call many times.</summary>
    private void RevealPage()
    {
        if (_pageRevealed) return;
        _pageRevealed = true;

        _revealFallback?.Stop();
        _web?.BringToFront();
        if (_status is not null) _status.Visible = false;
    }

    private void StopLoadingSpinner()
    {
        _startingBrowser = false;
        _navigating = false;
        UpdateLoadingState();
    }

    /// <summary>Throw away a control that never finished starting up.</summary>
    private void Discard(WebView2? view)
    {
        if (view is null || ReferenceEquals(view, _web)) return;
        try
        {
            Controls.Remove(view);
            view.Dispose();
        }
        catch { /* it never worked in the first place */ }
    }

    private void ShowFatal(string message)
    {
        if (_status is not null)
        {
            _status.Visible = true;
            _status.Text = message;
        }
        Dialogs.Error(message);
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
        {
            // The browser process died. Rebuild it on the next time the window is shown.
            StopLoadingSpinner();
            DisposeWebView();
            if (Visible && WindowState != FormWindowState.Minimized) EnsureWebView();
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        _navigating = true;
        UpdateLoadingState();

        if (!_cfg.ExternalLinksInBrowser) return;
        if (!e.IsUserInitiated) return;

        var host = TryHost(e.Uri);
        if (host is null || _startHost is null) return;
        if (string.Equals(host, _startHost, StringComparison.OrdinalIgnoreCase)) return;

        e.Cancel = true;
        _navigating = false;
        UpdateLoadingState();
        WebView2MissingForm.OpenUrl(e.Uri);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        _navigating = false;
        UpdateLoadingState();
        RevealPage();   // also covers a page that failed to load: show its error page
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (_cfg.ExternalLinksInBrowser)
            WebView2MissingForm.OpenUrl(e.Uri);
        else
            _web?.CoreWebView2?.Navigate(e.Uri);   // keep everything in the one window
    }

    private static string? TryHost(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host : null;

    private void RememberUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;
        _currentUrl = url;
        _session.LastUrl = url;
    }

    private void DisposeWebView()
    {
        if (_web is null) return;
        try
        {
            _web.CoreWebView2?.Stop();
            Controls.Remove(_web);
            _web.Dispose();
        }
        catch { /* nothing useful to do while tearing down */ }
        _web = null;
        _pageRevealed = false;
        _revealFallback?.Stop();
    }

    /// <summary>Drop the embedded browser and hand the memory back to Windows.</summary>
    private void SleepNow()
    {
        if (_web is null) { _sleep.MarkAsleep(); return; }
        if (Visible && WindowState != FormWindowState.Minimized) return;   // safety net

        StopLoadingSpinner();
        DisposeWebView();
        _sleep.MarkAsleep();
        SaveSession();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        NativeMethods.TrimWorkingSet();
    }

    // ---------------------------------------------------------- show and hide

    private void ShowFromTray()
    {
        _hiddenInTray = false;
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = _restoreState;
        Activate();
        NativeMethods.SetForegroundWindow(Handle);

        _sleep.MarkActive();
        EnsureWebView();
    }

    private void HideToTray()
    {
        RememberBounds();
        _hiddenInTray = true;
        ClearReportedSize();
        // Hide() alone is enough: a hidden form has no taskbar button either way.
        // Touching ShowInTaskbar would be worse than useless here - WinForms answers
        // it by destroying and recreating the window handle, and the WebView2 child
        // cannot be reparented, so the show/hide cycle would throw
        // "Failed to set Win32 parent window of the Control".
        Hide();
        _sleep.MarkInactive();
        SaveSession();
    }

    private void OpenInBrowser() => WebView2MissingForm.OpenUrl(_currentUrl);

    private void ExitApp()
    {
        _exiting = true;
        Close();
    }

    /// <summary>Decides what the X button does: "auto" follows whether the tray is on.</summary>
    private bool CloseGoesToTray()
    {
        if (!_cfg.TrayEnabled) return false;
        return (_cfg.CloseAction ?? "auto").Trim().ToLowerInvariant() switch
        {
            "exit" => false,
            "tray" => true,
            _ => true      // auto + tray enabled -> behave like a normal tray app
        };
    }

    // ------------------------------------------------------------- overrides

    protected override void SetVisibleCore(bool value)
    {
        if (!_initialShowHandled)
        {
            _initialShowHandled = true;
            if (_startHidden)
            {
                // Create the window handle but never map it on screen, so there is no flash.
                if (!IsHandleCreated) CreateHandle();
                _sleep?.MarkInactive();
                base.SetVisibleCore(false);
                return;
            }
        }
        base.SetVisibleCore(value);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            var cfg = _cfgOrNull;
            // No "close" tag -> Windows draws a real, greyed out X and blocks Alt+F4.
            if (cfg is not null && cfg.HasTitleBar && !cfg.ShowClose)
                cp.ClassStyle |= NativeMethods.CS_NOCLOSE;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (_cfg.HasTitleBar && !_cfg.ShowClose)
        {
            var menu = NativeMethods.GetSystemMenu(Handle, false);
            if (menu != IntPtr.Zero)
                NativeMethods.EnableMenuItem(menu, NativeMethods.SC_CLOSE,
                    NativeMethods.MF_BYCOMMAND | NativeMethods.MF_GRAYED);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        if (WindowState == FormWindowState.Minimized)
        {
            if (_cfg.TrayEnabled && _cfg.MinimizeToTray) HideToTray();
            else _sleep?.MarkInactive();
        }
        else
        {
            _restoreState = WindowState;
            _sleep?.MarkActive();
            ReportSize();
            if (Visible) EnsureWebView();
        }
    }

    /// <summary>
    /// Puts "1100x820" after the title while the window is being resized, and takes it
    /// away again a second after the last change.
    ///
    /// The numbers are the whole window, borders included, which is exactly what the
    /// "width" and "height" settings mean, so what you read here can be pasted
    /// straight into the .conf file.
    /// </summary>
    private void ReportSize()
    {
        if (!_cfg.ShowSizeOnResize) return;
        if (!Visible || _lastReportedSize is null) return;

        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) return;

        // Only a genuine size change is worth announcing. Coming back from the tray or
        // from minimized restores the old size, and that is not news.
        if (size == _lastReportedSize.Value) return;
        _lastReportedSize = size;

        _resizeSize = $"{size.Width}x{size.Height}";
        UpdateTitle();

        _resizeHideTimer ??= CreateResizeHideTimer();
        _resizeHideTimer.Stop();
        _resizeHideTimer.Start();
    }

    private System.Windows.Forms.Timer CreateResizeHideTimer()
    {
        var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _resizeSize = null;
            UpdateTitle();
        };
        return timer;
    }

    private void ClearReportedSize()
    {
        _resizeHideTimer?.Stop();
        if (_resizeSize is null) return;
        _resizeSize = null;
        UpdateTitle();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // WinForms only reports UserClosing when the close came through the system menu
        // (the X button, Alt+F4). A plain WM_CLOSE posted by another program arrives as
        // None. Both mean "the user wants this window gone", so treat them alike. Our
        // own Exit sets _exiting first, and shutdown/Application.Exit have their own
        // reasons, so nothing here can trap the app.
        bool userAsked = e.CloseReason is CloseReason.UserClosing or CloseReason.None;
        if (!_exiting && userAsked && CloseGoesToTray())
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        RememberBounds();
        SaveSession();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        _sleep?.Dispose();
        _loading?.Dispose();
        _revealFallback?.Dispose();
        _resizeHideTimer?.Dispose();
        DisposeWebView();
        _uiMarshal.Dispose();
        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        // Borderless windows still need resize borders.
        if (m.Msg == NativeMethods.WM_NCHITTEST && !_cfg.HasTitleBar && _cfg.Resizable
            && WindowState == FormWindowState.Normal)
        {
            base.WndProc(ref m);
            if ((int)m.Result == NativeMethods.HTCLIENT)
            {
                int hit = HitTestBorder(new Point((short)(long)m.LParam, (short)((long)m.LParam >> 16)));
                if (hit != NativeMethods.HTCLIENT) m.Result = hit;
            }
            return;
        }

        base.WndProc(ref m);
    }

    private const int BorderGrip = 6;

    private int HitTestBorder(Point screenPoint)
    {
        var p = PointToClient(screenPoint);
        bool left = p.X <= BorderGrip;
        bool right = p.X >= ClientSize.Width - BorderGrip;
        bool top = p.Y <= BorderGrip;
        bool bottom = p.Y >= ClientSize.Height - BorderGrip;

        if (top && left) return NativeMethods.HTTOPLEFT;
        if (top && right) return NativeMethods.HTTOPRIGHT;
        if (bottom && left) return NativeMethods.HTBOTTOMLEFT;
        if (bottom && right) return NativeMethods.HTBOTTOMRIGHT;
        if (left) return NativeMethods.HTLEFT;
        if (right) return NativeMethods.HTRIGHT;
        if (top) return NativeMethods.HTTOP;
        if (bottom) return NativeMethods.HTBOTTOM;
        return NativeMethods.HTCLIENT;
    }

    // --------------------------------------------------------------- session

    private void RememberBounds()
    {
        if (_hiddenInTray && !Visible && _session.Window.X.HasValue) return;

        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        _session.Window.X = bounds.X;
        _session.Window.Y = bounds.Y;
        _session.Window.Width = bounds.Width;
        _session.Window.Height = bounds.Height;
        _session.Window.State = WindowState == FormWindowState.Maximized ? "max" : "normal";
    }

    private void SaveSession() => _session.Save();
}
