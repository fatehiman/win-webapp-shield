namespace WebAppShield;

/// <summary>
/// Optional trace file, for the questions that are hard to answer from the outside:
/// why did the window not go to the tray, why did the browser restart, and so on.
///
/// Off unless the environment variable WWS_TRACE is set to something, so it costs a
/// single string comparison at startup and nothing after that. When it is on, lines
/// are appended to &lt;exename&gt;.trace.log next to the exe.
/// </summary>
internal static class TraceLog
{
    private static readonly bool Enabled =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WWS_TRACE"));

    private static readonly object Gate = new();
    private static string? _path;

    public static bool IsOn => Enabled;

    public static void Log(string message)
    {
        if (!Enabled) return;
        try
        {
            _path ??= Path.Combine(Program.ExeDir, Program.ExeName + ".trace.log");
            lock (Gate)
            {
                File.AppendAllText(_path, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Tracing must never be the thing that breaks the app.
        }
    }
}
