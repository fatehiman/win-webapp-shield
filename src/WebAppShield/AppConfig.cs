using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebAppShield;

/// <summary>Everything that can be set in the &lt;exename&gt;.conf file.</summary>
public sealed class AppConfig
{
    private static readonly char[] TagSeparators = { '+', ',', ' ', '|' };
    private static readonly char[] PointSeparators = { ',', ';', 'x', ' ' };

    // ---- A) page ---------------------------------------------------------
    [JsonPropertyName("url")]
    public string Url { get; set; } = "about:blank";

    // ---- B) initial size -------------------------------------------------
    [JsonPropertyName("width")]
    public int Width { get; set; } = 1024;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 768;

    // ---- C) monitor ------------------------------------------------------
    /// <summary>0 = default/primary monitor, 1 = "Display 1", 2 = "Display 2" ...</summary>
    [JsonPropertyName("monitor")]
    public int Monitor { get; set; }

    // ---- D) window chrome ------------------------------------------------
    /// <summary>"" = no title bar. Otherwise any combination of min, max, close, tray.</summary>
    [JsonPropertyName("window-type")]
    public string WindowType { get; set; } = "min+max+close";

    // ---- E) initial position --------------------------------------------
    /// <summary>"center", "last", or an explicit "x,y".</summary>
    [JsonPropertyName("position")]
    public string Position { get; set; } = "center";

    // ---- F) initial window state ----------------------------------------
    /// <summary>"normal", "min", "max" or "tray".</summary>
    [JsonPropertyName("window-state")]
    public string WindowState { get; set; } = "normal";

    // ---- G) icon ---------------------------------------------------------
    /// <summary>.ico file, absolute or relative to the exe folder. Empty = Windows default.</summary>
    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "";

    // ---- H) tray ---------------------------------------------------------
    [JsonPropertyName("systray")]
    public bool Systray { get; set; }

    // ---- I) title --------------------------------------------------------
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    // ---- J) tray menu ----------------------------------------------------
    [JsonPropertyName("tray-open-in-browser")]
    public bool TrayOpenInBrowser { get; set; }

    // ---- K) memory -------------------------------------------------------
    /// <summary>"auto", "off", or a number of minutes.</summary>
    [JsonPropertyName("sleep-after")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string SleepAfter { get; set; } = "auto";

    // ---- extras ----------------------------------------------------------
    /// <summary>Spinner shown after the title while a page loads: spinner, bar, dots, braille, off.</summary>
    [JsonPropertyName("loading-indicator")]
    public string LoadingIndicator { get; set; } = "spinner";

    /// <summary>What the X button does: "auto" (tray if the tray is on), "tray" or "exit".</summary>
    [JsonPropertyName("close-action")]
    public string CloseAction { get; set; } = "auto";

    [JsonPropertyName("resizable")]
    public bool Resizable { get; set; } = true;

    [JsonPropertyName("always-on-top")]
    public bool AlwaysOnTop { get; set; }

    /// <summary>When the tray icon is on: does Minimize also hide the window into the tray?</summary>
    [JsonPropertyName("minimize-to-tray")]
    public bool MinimizeToTray { get; set; } = true;

    [JsonPropertyName("remember-size")]
    public bool RememberSize { get; set; } = true;

    [JsonPropertyName("zoom")]
    public double Zoom { get; set; } = 1.0;

    [JsonPropertyName("user-agent")]
    public string UserAgent { get; set; } = "";

    /// <summary>"error" = show a message and quit. "focus" = show the running window and quit quietly.</summary>
    [JsonPropertyName("single-instance-action")]
    public string SingleInstanceAction { get; set; } = "error";

    /// <summary>Open links that leave the start page's host in the default browser instead.</summary>
    [JsonPropertyName("external-links-in-browser")]
    public bool ExternalLinksInBrowser { get; set; }

    [JsonPropertyName("context-menu")]
    public bool ContextMenu { get; set; } = true;

    [JsonPropertyName("dev-tools")]
    public bool DevTools { get; set; }

    /// <summary>Browser profile folder. Empty = %LOCALAPPDATA%\WinWebAppShield\&lt;exe&gt;-&lt;hash&gt;.</summary>
    [JsonPropertyName("data-dir")]
    public string DataDir { get; set; } = "";

    /// <summary>Folder of a fixed-version WebView2 runtime. Empty = use the installed one.</summary>
    [JsonPropertyName("webview2-path")]
    public string WebView2Path { get; set; } = "";

    [JsonPropertyName("webview2-args")]
    public string WebView2Args { get; set; } = "";

    // ---- derived ---------------------------------------------------------

    [JsonIgnore] public bool HasTitleBar => !string.IsNullOrWhiteSpace(WindowType);
    [JsonIgnore] public bool ShowMinimize => HasTag("min");
    [JsonIgnore] public bool ShowMaximize => HasTag("max");
    [JsonIgnore] public bool ShowClose => HasTag("close");
    [JsonIgnore] public bool CloseToTray => HasTag("tray");

    /// <summary>H) says: if any other setting implies the tray, the tray is on.</summary>
    [JsonIgnore]
    public bool TrayEnabled => Systray || CloseToTray
                               || string.Equals(WindowState, "tray", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string EffectiveTitle => string.IsNullOrWhiteSpace(Title) ? Program.ExeName : Title;

    private bool HasTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(WindowType)) return false;
        foreach (var part in WindowType.Split(TagSeparators, StringSplitOptions.RemoveEmptyEntries))
            if (part.Trim().Equals(tag, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Reports settings that are not understood, so the user can be warned.</summary>
    public List<string> Validate()
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(Url))
            problems.Add("\"url\" is empty.");
        else if (!Uri.TryCreate(Url, UriKind.Absolute, out _))
            problems.Add("\"url\" is not a valid absolute address: " + Url);

        if (Width < 100 || Width > 30000) problems.Add($"\"width\" must be between 100 and 30000 (got {Width}).");
        if (Height < 100 || Height > 30000) problems.Add($"\"height\" must be between 100 and 30000 (got {Height}).");
        if (Monitor < 0) problems.Add($"\"monitor\" must be 0 or higher (got {Monitor}).");

        if (!string.IsNullOrWhiteSpace(WindowType))
        {
            foreach (var part in WindowType.Split(TagSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                var t = part.Trim().ToLowerInvariant();
                if (t != "min" && t != "max" && t != "close" && t != "tray")
                    problems.Add($"\"window-type\" contains unknown tag \"{part}\". Allowed: min, max, close, tray.");
            }
        }

        if (!IsOneOf(WindowState, "normal", "min", "max", "tray"))
            problems.Add($"\"window-state\" must be normal, min, max or tray (got \"{WindowState}\").");

        if (!IsOneOf(Position, "center", "last") && ParsePoint(Position) is null)
            problems.Add($"\"position\" must be center, last or \"x,y\" (got \"{Position}\").");

        if (!IsOneOf(SleepAfter, "auto", "off", "never") && ParseMinutes(SleepAfter) is null)
            problems.Add($"\"sleep-after\" must be auto, off, or a number of minutes (got \"{SleepAfter}\").");

        if (!IsOneOf(LoadingIndicator, "off", "none")
            && !WebAppShield.LoadingIndicator.Styles.ContainsKey(LoadingIndicator?.Trim() ?? ""))
        {
            problems.Add($"\"loading-indicator\" must be one of " +
                         string.Join(", ", WebAppShield.LoadingIndicator.Styles.Keys) +
                         $", or off (got \"{LoadingIndicator}\").");
        }

        if (!IsOneOf(CloseAction, "auto", "tray", "exit"))
            problems.Add($"\"close-action\" must be auto, tray or exit (got \"{CloseAction}\").");

        if (!IsOneOf(SingleInstanceAction, "error", "focus"))
            problems.Add($"\"single-instance-action\" must be error or focus (got \"{SingleInstanceAction}\").");

        if (Zoom < 0.25 || Zoom > 5.0)
            problems.Add($"\"zoom\" must be between 0.25 and 5.0 (got {Zoom}).");

        return problems;
    }

    private static bool IsOneOf(string? value, params string[] allowed)
        => allowed.Any(a => string.Equals(value?.Trim(), a, StringComparison.OrdinalIgnoreCase));

    public static Point? ParsePoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Split(PointSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return null;
        if (int.TryParse(parts[0].Trim(), out int x) && int.TryParse(parts[1].Trim(), out int y))
            return new Point(x, y);
        return null;
    }

    public static int? ParseMinutes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (double.TryParse(value.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double m) && m >= 0)
            return (int)Math.Round(m);
        return null;
    }
}

/// <summary>Lets a setting be written either as "120" or as 120 in the JSON file.</summary>
public sealed class StringOrNumberConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String: return reader.GetString() ?? "";
            case JsonTokenType.Number:
                return reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
            case JsonTokenType.True: return "true";
            case JsonTokenType.False: return "false";
            case JsonTokenType.Null: return "";
            default: throw new JsonException("Expected a string or a number.");
        }
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}
