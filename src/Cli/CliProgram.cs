using System.Text;
using Shield.Crypto;

namespace Shield.Cli;

internal static class CliProgram
{
#if DECODE_MODE
    private const bool Decoding = true;
    private const string ToolName = "decode";
    private const string Verb = "Decrypt";
#else
    private const bool Decoding = false;
    private const string ToolName = "encode";
    private const string Verb = "Encrypt";
#endif

    private const int ExitOk = 0;
    private const int ExitUsage = 1;
    private const int ExitFileError = 2;
    private const int ExitCryptoError = 3;

    public static int Main(string[] rawArgs)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var args = rawArgs.ToList();
        bool force = false, noBackup = false;
        for (int i = args.Count - 1; i >= 0; i--)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "-h" or "--help" or "/?":
                    PrintUsage();
                    return ExitOk;
                case "-f" or "--force":
                    force = true; args.RemoveAt(i); break;
                case "--no-backup":
                    noBackup = true; args.RemoveAt(i); break;
            }
        }

        if (args.Count is < 2 or > 3)
        {
            PrintUsage();
            return ExitUsage;
        }

        string inputPath = args[0];
        string password = args[1];
        string? outputPath = args.Count == 3 ? args[2] : null;
        bool inPlace = outputPath is null;

        if (string.IsNullOrEmpty(password))
        {
            Error("Password must not be empty.");
            return ExitUsage;
        }

        byte[] input;
        try
        {
            inputPath = Path.GetFullPath(inputPath);
            input = File.ReadAllBytes(inputPath);
        }
        catch (Exception ex)
        {
            Error($"Cannot read \"{inputPath}\": {ex.Message}");
            return ExitFileError;
        }

        byte[] output;
        try
        {
            if (Decoding)
            {
                if (!ConfCrypto.IsEncrypted(input))
                {
                    Error($"\"{inputPath}\" is not an encrypted file (missing WASENC1 header).");
                    return ExitCryptoError;
                }
                output = ConfCrypto.Decrypt(input, password);
            }
            else
            {
                if (ConfCrypto.IsEncrypted(input) && !force)
                {
                    Error($"\"{inputPath}\" is already encrypted. Use --force to encrypt it again.");
                    return ExitCryptoError;
                }
                output = ConfCrypto.Encrypt(input, password);
            }
        }
        catch (Exception ex)
        {
            Error(ex.Message);
            return ExitCryptoError;
        }

        string target = inPlace ? inputPath : Path.GetFullPath(outputPath!);
        try
        {
            if (inPlace && !noBackup)
            {
                string backup = inputPath + ".bak";
                File.Copy(inputPath, backup, overwrite: true);
                Console.WriteLine($"Backup written: {backup}");
            }
            else if (!inPlace && File.Exists(target) && !force)
            {
                Error($"\"{target}\" already exists. Use --force to overwrite it.");
                return ExitFileError;
            }

            string? dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(target, output);
        }
        catch (Exception ex)
        {
            Error($"Cannot write \"{target}\": {ex.Message}");
            return ExitFileError;
        }

        Console.WriteLine($"{Verb}ed {input.Length} bytes -> {output.Length} bytes");
        Console.WriteLine($"Output: {target}");
        return ExitOk;
    }

    private static void Error(string message)
    {
        var old = Console.ForegroundColor;
        try { Console.ForegroundColor = ConsoleColor.Red; } catch { /* redirected */ }
        Console.Error.WriteLine("ERROR: " + message);
        try { Console.ForegroundColor = old; } catch { /* redirected */ }
    }

    private static void PrintUsage()
    {
        string what = Decoding
            ? "Decrypts a WASENC1 file (for example a win-webapp-shield .conf file)."
            : "Encrypts a file with AES-256-GCM (for example a win-webapp-shield .conf file).";

        Console.WriteLine($"""
            {ToolName}.exe - {what}

            USAGE
              {ToolName}.exe "<file>" "<password>" ["<output file>"] [options]

            ARGUMENTS
              <file>          Path to the input file. Put it inside double quotes.
              <password>      Password. Put it inside double quotes.
              <output file>   Optional. Where to write the result.
                              If you leave it out, the input file is replaced and a
                              "<file>.bak" backup is created next to it.

            OPTIONS
              -f, --force     Overwrite an existing output file, or {(Decoding ? "ignore" : "re-encrypt an already encrypted file")}.
              --no-backup     Do not create the .bak file when writing in place.
              -h, --help      Show this help.

            EXAMPLES
              {ToolName}.exe "C:\apps\mstodo\mstodo.conf" "Kopolop0_90u"
              {ToolName}.exe "mstodo.conf" "Kopolop0_90u" "mstodo.{(Decoding ? "plain" : "enc")}"

            CRYPTO
              AES-256-GCM, key derived from the password with PBKDF2-HMAC-SHA256
              ({ConfCrypto.DefaultIterations:N0} iterations, 16 byte random salt, 12 byte random nonce).
              GCM is authenticated, so a wrong password or a damaged file is reported
              instead of producing garbage output.

            EXIT CODES
              0 success   1 bad arguments   2 file error   3 crypto error
            """);
    }
}
