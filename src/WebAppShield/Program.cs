using Microsoft.Web.WebView2.Core;

namespace WebAppShield;

internal static class Program
{
    /// <summary>Full path of the running exe.</summary>
    internal static string ExePath { get; private set; } = "";

    /// <summary>Folder that holds the exe. The .conf and .session files live here.</summary>
    internal static string ExeDir { get; private set; } = "";

    /// <summary>Exe file name without the extension, e.g. "mstodo".</summary>
    internal static string ExeName { get; private set; } = "app";

    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        ResolvePaths();

        Application.ThreadException += (_, e) => ReportCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject as Exception);

        // --- 2) the .conf file next to the exe, with the same name -----------
        string confPath = Path.Combine(ExeDir, ExeName + ".conf");
        ConfigLoader.Result loaded;
        try
        {
            loaded = ConfigLoader.Load(confPath);
        }
        catch (ConfigException ex)
        {
            Dialogs.Error(ex.Message, ExeName + " - Configuration error");
            return 2;
        }

        var cfg = loaded.Config;

        if (loaded.Warnings.Count > 0)
        {
            Dialogs.Warn(
                "Some settings in the configuration file are not valid. The built in defaults " +
                "are used for them." + Environment.NewLine + Environment.NewLine +
                confPath + Environment.NewLine + Environment.NewLine +
                "- " + string.Join(Environment.NewLine + "- ", loaded.Warnings),
                cfg.EffectiveTitle + " - Configuration warning");
        }

        // --- 3) only one copy of this exact exe file -------------------------
        using var instance = new SingleInstance(ExePath);
        if (!instance.TryAcquire())
        {
            if (cfg.SingleInstanceAction.Trim().Equals("focus", StringComparison.OrdinalIgnoreCase)
                && instance.SignalRunningInstance())
            {
                return 0;
            }

            Dialogs.Error(
                "This application is already running." + Environment.NewLine + Environment.NewLine +
                ExePath + Environment.NewLine + Environment.NewLine +
                "Only one copy of this exe file can run at a time. Use the window that is " +
                "already open, or look for the icon in the notification area." + Environment.NewLine +
                Environment.NewLine +
                "Tip: to run a second copy at the same time, put the exe and its .conf file in " +
                "a different folder and start it from there.",
                cfg.EffectiveTitle + " - Already running");
            return 3;
        }

        // --- session scratch file -------------------------------------------
        var session = SessionStore.Load(Path.Combine(ExeDir, ExeName + ".session"));

        // --- M) is the embedded browser there? ------------------------------
        string? browserFolder = ResolveBrowserFolder(cfg, session, out bool runtimeMissing);
        if (runtimeMissing) return 4;

        string userDataFolder = ResolveUserDataFolder(cfg, instance.Key);

        using var form = new MainForm(cfg, session, instance, userDataFolder, browserFolder);
        Application.Run(form);
        return 0;
    }

    /// <summary>
    /// Shows an unexpected error and, when the folder allows it, appends the full
    /// details to &lt;exename&gt;.error.log so a user can send them on.
    /// </summary>
    private static void ReportCrash(Exception? ex)
    {
        string details = ex?.ToString() ?? "Unknown error.";
        string? logPath = null;
        try
        {
            logPath = Path.Combine(ExeDir, ExeName + ".error.log");
            File.AppendAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {details}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            logPath = null;   // read only folder: the message box is all the user gets
        }

        string message = "Unexpected error:" + Environment.NewLine + Environment.NewLine +
                         (ex?.Message ?? "Unknown error.") + Environment.NewLine + Environment.NewLine +
                         Shorten(details, 1200);

        if (logPath is not null)
            message += Environment.NewLine + Environment.NewLine + "Full details were written to:" +
                       Environment.NewLine + logPath;

        Dialogs.Error(message);
    }

    private static string Shorten(string text, int max)
        => text.Length <= max ? text : text[..max] + "...";

    private static void ResolvePaths()
    {
        // Environment.ProcessPath is the real exe even for a single file build.
        ExePath = Environment.ProcessPath ?? Application.ExecutablePath;
        ExeDir = Path.GetDirectoryName(ExePath) ?? AppContext.BaseDirectory;
        ExeName = Path.GetFileNameWithoutExtension(ExePath);
        if (string.IsNullOrWhiteSpace(ExeName)) ExeName = "app";
    }

    /// <summary>
    /// Returns null to use the WebView2 runtime installed on the machine, or the folder of a
    /// fixed version runtime. Sets <paramref name="runtimeMissing"/> when nothing usable was
    /// found and the user closed the dialog without choosing a folder.
    /// </summary>
    private static string? ResolveBrowserFolder(AppConfig cfg, SessionStore session, out bool runtimeMissing)
    {
        runtimeMissing = false;
        foreach (var candidate in new[] { cfg.WebView2Path, session.WebView2Path })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            string full = Path.IsPathRooted(candidate) ? candidate : Path.Combine(ExeDir, candidate);
            if (WebView2MissingForm.IsRuntimeFolder(full)) return full;
        }

        string details;
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            if (!string.IsNullOrEmpty(version)) return null;      // installed runtime is fine
            details = "Detected version: (none)";
        }
        catch (WebView2RuntimeNotFoundException)
        {
            details = "Windows reports that no WebView2 runtime is registered.";
        }
        catch (Exception ex)
        {
            details = "Detection failed: " + ex.Message;
        }

        using var dialog = new WebView2MissingForm(details);
        if (dialog.ShowDialog() == DialogResult.OK && dialog.ChosenPath is { } chosen)
        {
            session.WebView2Path = chosen;
            session.Save();      // remembered for the next run; ignored if the folder is read only
            return chosen;
        }

        runtimeMissing = true;
        return null;
    }

    private static string ResolveUserDataFolder(AppConfig cfg, string instanceKey)
    {
        string candidate;
        if (!string.IsNullOrWhiteSpace(cfg.DataDir))
        {
            candidate = Path.IsPathRooted(cfg.DataDir) ? cfg.DataDir : Path.Combine(ExeDir, cfg.DataDir);
        }
        else
        {
            // Keyed by the exe path, so two copies in two folders keep separate profiles.
            candidate = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinWebAppShield", ExeName + "-" + instanceKey);
        }

        try
        {
            Directory.CreateDirectory(candidate);
            return candidate;
        }
        catch
        {
            string fallback = Path.Combine(Path.GetTempPath(), "WinWebAppShield", ExeName + "-" + instanceKey);
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }
}
