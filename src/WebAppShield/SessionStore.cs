using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebAppShield;

/// <summary>
/// Plain JSON scratch file written next to the exe as &lt;exename&gt;.session.
/// It is never required. If the folder is read only, every write is ignored in silence.
/// </summary>
public sealed class SessionStore
{
    [JsonPropertyName("window")] public WindowState Window { get; set; } = new();
    [JsonPropertyName("lastUrl")] public string LastUrl { get; set; } = "";
    [JsonPropertyName("webview2Path")] public string WebView2Path { get; set; } = "";
    [JsonPropertyName("savedAt")] public string SavedAt { get; set; } = "";

    public sealed class WindowState
    {
        [JsonPropertyName("x")] public int? X { get; set; }
        [JsonPropertyName("y")] public int? Y { get; set; }
        [JsonPropertyName("width")] public int? Width { get; set; }
        [JsonPropertyName("height")] public int? Height { get; set; }
        /// <summary>"normal" or "max". Minimized is never restored as minimized.</summary>
        [JsonPropertyName("state")] public string State { get; set; } = "normal";
    }

    [JsonIgnore] public string FilePath { get; private set; } = "";
    [JsonIgnore] public bool Writable { get; private set; } = true;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static SessionStore Load(string path)
    {
        SessionStore store;
        try
        {
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path);
                store = JsonSerializer.Deserialize<SessionStore>(text, Options) ?? new SessionStore();
            }
            else
            {
                store = new SessionStore();
            }
        }
        catch
        {
            // A damaged session file must never stop the application.
            store = new SessionStore();
        }

        store.FilePath = path;
        store.Window ??= new WindowState();
        return store;
    }

    /// <summary>Best effort write. Returns false if it could not be saved.</summary>
    public bool Save()
    {
        if (string.IsNullOrEmpty(FilePath)) return false;
        try
        {
            SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var json = JsonSerializer.Serialize(this, Options);
            // Write to a temp file first so a crash cannot leave a half written file.
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            File.Move(tmp, FilePath, overwrite: true);
            Writable = true;
            return true;
        }
        catch
        {
            Writable = false;
            return false;
        }
    }
}
