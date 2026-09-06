using System.Text.Json;
using Shield.Crypto;

namespace WebAppShield;

/// <summary>Finds, decrypts and parses the &lt;exename&gt;.conf file.</summary>
public static class ConfigLoader
{
    /// <summary>
    /// The key used to decrypt an encrypted .conf file.
    /// It is deliberately hardcoded: the goal is to keep the file unreadable to a
    /// casual user, not to protect it from someone who can read the exe.
    /// </summary>
    public const string ConfPassword = "Kopolop0_90u";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public sealed class Result
    {
        public required AppConfig Config { get; init; }
        public required string Path { get; init; }
        public bool WasEncrypted { get; init; }
        public List<string> Warnings { get; init; } = new();
    }

    /// <summary>Throws <see cref="ConfigException"/> with a message meant for a message box.</summary>
    public static Result Load(string confPath)
    {
        if (!File.Exists(confPath))
        {
            throw new ConfigException(
                "Configuration file not found." + Environment.NewLine + Environment.NewLine +
                confPath + Environment.NewLine + Environment.NewLine +
                "This application looks for a .conf file that sits next to the .exe and has the " +
                "same name. Create it, then start the application again.");
        }

        byte[] raw;
        try
        {
            raw = File.ReadAllBytes(confPath);
        }
        catch (Exception ex)
        {
            throw new ConfigException(
                "Configuration file could not be read." + Environment.NewLine + Environment.NewLine +
                confPath + Environment.NewLine + Environment.NewLine + ex.Message);
        }

        bool encrypted = ConfCrypto.IsEncrypted(raw);
        string text;
        if (encrypted)
        {
            try
            {
                text = ConfCrypto.DecryptText(raw, ConfPassword);
            }
            catch (Exception ex)
            {
                throw new ConfigException(
                    "Configuration file is encrypted but could not be decrypted." + Environment.NewLine +
                    Environment.NewLine + confPath + Environment.NewLine + Environment.NewLine + ex.Message);
            }
        }
        else
        {
            text = ConfCrypto.StripBom(raw);
        }

        if (string.IsNullOrWhiteSpace(text))
            throw new ConfigException("Configuration file is empty:" + Environment.NewLine + Environment.NewLine + confPath);

        AppConfig? cfg;
        try
        {
            cfg = JsonSerializer.Deserialize<AppConfig>(text, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ConfigException(
                "Configuration file is not valid JSON." + Environment.NewLine + Environment.NewLine +
                confPath + Environment.NewLine + Environment.NewLine +
                Describe(ex) + Environment.NewLine + Environment.NewLine +
                "Note: // and /* */ comments and trailing commas are allowed. Single quotes are not.");
        }

        if (cfg is null)
            throw new ConfigException("Configuration file did not contain a JSON object:" +
                                      Environment.NewLine + Environment.NewLine + confPath);

        return new Result
        {
            Config = cfg,
            Path = confPath,
            WasEncrypted = encrypted,
            Warnings = cfg.Validate()
        };
    }

    private static string Describe(JsonException ex)
    {
        if (ex.LineNumber.HasValue)
            return $"Line {ex.LineNumber + 1}, position {ex.BytePositionInLine + 1}: {ex.Message}";
        return ex.Message;
    }
}

public sealed class ConfigException : Exception
{
    public ConfigException(string message) : base(message) { }
}
