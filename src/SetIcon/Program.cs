using Shield.SetIcon;

// set-icon.exe
//
// Drop it in the folder of a wrapped app, run it, and the exe there gets the icon
// of the .ico file next to it. It is the no rebuild version of build.ps1 -AppIcon.

const string Usage = """
set-icon - gives an exe the icon of an .ico file, in place.

  set-icon                     the first .ico in this folder, and the exe of the
                               same name, e.g. mstodo.ico -> mstodo.exe
  set-icon <file.ico>          that icon, and the exe of the same name
  set-icon <file.ico> <a.exe>  that icon and that exe
  set-icon -h                  this help

Windows caches icons, so Explorer can keep showing the old one for a while. The
taskbar and Alt+Tab pick the new icon up as soon as the app starts again.
""";

int exitCode = Run();

// Started from Explorer the console closes with us, and the message would be gone
// before it could be read. Started from a prompt there is nothing to wait for.
if (StartedFromExplorer())
{
    Console.WriteLine();
    Console.Write("Press any key to close.");
    Console.ReadKey(true);
}

return exitCode;

int Run()
{
try
{
    if (args.Length > 0 && (args[0] is "-h" or "--help" or "/?" or "-?"))
    {
        Console.WriteLine(Usage);
        return 0;
    }

    if (args.Length > 2)
    {
        Console.Error.WriteLine("Too many arguments.");
        Console.Error.WriteLine();
        Console.Error.WriteLine(Usage);
        return 2;
    }

    string folder = AppContext.BaseDirectory;

    string iconPath = args.Length >= 1
        ? Path.GetFullPath(args[0])
        : FindFirstIcon(folder);

    if (!File.Exists(iconPath))
    {
        Console.Error.WriteLine($"Icon file not found: {iconPath}");
        return 1;
    }

    string exePath = args.Length >= 2
        ? Path.GetFullPath(args[1])
        : Path.ChangeExtension(iconPath, ".exe");

    if (!File.Exists(exePath))
    {
        Console.Error.WriteLine($"No exe with the same name as the icon: {exePath}");
        Console.Error.WriteLine("Rename the .ico to match the exe, or name both files on the command line.");
        return 1;
    }

    if (IsSelf(exePath))
    {
        Console.Error.WriteLine("That is set-icon itself. Point it at the app exe instead.");
        return 1;
    }

    List<IconImage> images = IconFile.Read(iconPath);

    Console.WriteLine($"Icon : {iconPath}");
    Console.WriteLine($"Exe  : {exePath}");
    foreach (IconImage image in images) Console.WriteLine($"       {image.Describe()}");

    ResourcePatcher.SetIcon(exePath, images);

    Console.WriteLine($"Done. {Path.GetFileName(exePath)} now carries {Path.GetFileName(iconPath)}.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("set-icon failed: " + ex.Message);
    return 1;
}
}

// Our own process is the only one on this console when Explorer started it; a shell
// makes at least two.
static bool StartedFromExplorer()
{
    try
    {
        uint[] ids = new uint[4];
        return NativeMethods.GetConsoleProcessList(ids, (uint)ids.Length) == 1;
    }
    catch (DllNotFoundException) { return false; }
    catch (EntryPointNotFoundException) { return false; }
}

// The .ico files are sorted by name, so the same folder always gives the same answer.
static string FindFirstIcon(string folder)
{
    string[] icons = Directory.GetFiles(folder, "*.ico", SearchOption.TopDirectoryOnly);
    if (icons.Length == 0)
        throw new FileNotFoundException($"No .ico file in {folder}. Put one next to set-icon.exe.");

    Array.Sort(icons, StringComparer.OrdinalIgnoreCase);
    return icons[0];
}

static bool IsSelf(string exePath)
{
    string self = Path.Combine(AppContext.BaseDirectory, Path.GetFileName(Environment.ProcessPath ?? ""));
    return string.Equals(Path.GetFullPath(exePath), Path.GetFullPath(self), StringComparison.OrdinalIgnoreCase);
}
