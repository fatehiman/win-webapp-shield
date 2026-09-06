using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WebAppShield.Setup;

/// <summary>
/// Installs the one thing this application cannot ship inside its own exe: the
/// Microsoft Edge WebView2 Runtime.
///
/// The runtime is already part of Windows 11 and of up to date Windows 10, so on most
/// machines this tool only has to say "nothing to do".
/// </summary>
internal static class Program
{
    /// <summary>Official evergreen bootstrapper. Small file, pulls the rest itself.</summary>
    private const string BootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    private const string ManualUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

    /// <summary>WebView2 Runtime product id inside the Edge Update registry keys.</summary>
    private const string ClientGuid = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    private static bool _silent;

    private static int Main(string[] args)
    {
        _silent = args.Any(a => a is "/silent" or "--silent" or "-s" or "/quiet" or "-y");
        bool help = args.Any(a => a is "/?" or "-h" or "--help");

        Title("Win WebApp Shield - prerequisites setup");

        if (help)
        {
            Console.WriteLine("""
                USAGE
                  setup.exe [options]

                OPTIONS
                  -s, --silent   Install without asking anything and without pausing.
                  -h, --help     Show this help.

                WHAT IT DOES
                  Checks for the Microsoft Edge WebView2 Runtime and installs it if it is
                  missing. Windows 11 already includes it, so usually there is nothing to do.
                """);
            return Pause(0);
        }

        string? version = FindRuntimeVersion();
        if (version is not null)
        {
            Ok($"Microsoft Edge WebView2 Runtime is already installed (version {version}).");
            Console.WriteLine("Nothing to do. You can start the application now.");
            return Pause(0);
        }

        Warn("Microsoft Edge WebView2 Runtime was not found on this computer.");
        Console.WriteLine("The application needs it to show web pages.");
        Console.WriteLine();

        if (!_silent && !Ask("Download and install it now? (Y/n) "))
        {
            Console.WriteLine();
            Console.WriteLine("No changes were made. You can install it by hand from:");
            Console.WriteLine("  " + ManualUrl);
            return Pause(1);
        }

        string temp = Path.Combine(Path.GetTempPath(), "MicrosoftEdgeWebview2Setup.exe");
        try
        {
            Console.WriteLine("Downloading the installer...");
            Download(BootstrapperUrl, temp);
            Ok($"Downloaded to {temp}");
        }
        catch (Exception ex)
        {
            Fail("Download failed: " + ex.Message);
            Console.WriteLine();
            Console.WriteLine("Check your internet connection, or install it by hand from:");
            Console.WriteLine("  " + ManualUrl);
            return Pause(2);
        }

        try
        {
            Console.WriteLine("Running the installer. Windows may ask for permission...");
            var psi = new ProcessStartInfo(temp)
            {
                UseShellExecute = true,
                Arguments = _silent ? "/silent /install" : "/install"
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit();

            if (proc is not null && proc.ExitCode != 0)
            {
                Fail($"The installer stopped with exit code {proc.ExitCode}.");
                return Pause(3);
            }
        }
        catch (Exception ex)
        {
            Fail("Could not run the installer: " + ex.Message);
            return Pause(3);
        }
        finally
        {
            try { File.Delete(temp); } catch { /* leftover in temp is harmless */ }
        }

        version = FindRuntimeVersion();
        if (version is null)
        {
            Warn("The installer finished but the runtime is still not registered.");
            Console.WriteLine("Restart the computer and run this setup again, or install by hand from:");
            Console.WriteLine("  " + ManualUrl);
            return Pause(4);
        }

        Ok($"Microsoft Edge WebView2 Runtime {version} is installed. You can start the application now.");
        return Pause(0);
    }

    /// <summary>Reads the version from the Edge Update registry keys. Null when not installed.</summary>
    internal static string? FindRuntimeVersion()
    {
        var places = new (RegistryKey Root, RegistryView View, string Path)[]
        {
            (Registry.LocalMachine, RegistryView.Registry32, $@"SOFTWARE\Microsoft\EdgeUpdate\Clients\{ClientGuid}"),
            (Registry.LocalMachine, RegistryView.Registry64, $@"SOFTWARE\Microsoft\EdgeUpdate\Clients\{ClientGuid}"),
            (Registry.CurrentUser,  RegistryView.Default,    $@"SOFTWARE\Microsoft\EdgeUpdate\Clients\{ClientGuid}")
        };

        foreach (var place in places)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(
                    place.Root == Registry.LocalMachine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser,
                    place.View);
                using var key = baseKey.OpenSubKey(place.Path);
                if (key?.GetValue("pv") is string pv && !string.IsNullOrWhiteSpace(pv) && pv != "0.0.0.0")
                    return pv;
            }
            catch
            {
                // A locked down registry just means "keep looking".
            }
        }
        return null;
    }

    private static void Download(string url, string target)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WinWebAppShield-Setup/1.0");
        using var response = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        using var input = response.Content.ReadAsStream();
        using var output = File.Create(target);
        input.CopyTo(output);
    }

    // ------------------------------------------------------------- console UI

    private static void Title(string text)
    {
        Console.WriteLine(text);
        Console.WriteLine(new string('=', text.Length));
        Console.WriteLine();
    }

    private static void Ok(string text) => Colored(ConsoleColor.Green, "[ ok ] " + text);
    private static void Warn(string text) => Colored(ConsoleColor.Yellow, "[warn] " + text);
    private static void Fail(string text) => Colored(ConsoleColor.Red, "[fail] " + text);

    private static void Colored(ConsoleColor color, string text)
    {
        try { Console.ForegroundColor = color; } catch { }
        Console.WriteLine(text);
        try { Console.ResetColor(); } catch { }
    }

    private static bool Ask(string prompt)
    {
        Console.Write(prompt);
        var line = Console.ReadLine();
        return string.IsNullOrWhiteSpace(line) || line.Trim().StartsWith('y') || line.Trim().StartsWith('Y');
    }

    /// <summary>Keep the window open when the user double clicked the exe in Explorer.</summary>
    private static int Pause(int exitCode)
    {
        if (_silent || !OwnsConsoleWindow()) return exitCode;
        Console.WriteLine();
        Console.Write("Press Enter to close...");
        try { Console.ReadLine(); } catch { }
        return exitCode;
    }

    private static bool OwnsConsoleWindow()
    {
        try
        {
            uint[] pids = new uint[2];
            uint count = GetConsoleProcessList(pids, 2);
            return count == 1;   // only this process uses the console -> Explorer started it
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleProcessList(uint[] processList, uint count);
}
