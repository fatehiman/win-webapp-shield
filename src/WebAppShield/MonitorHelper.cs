using System.Text.RegularExpressions;

namespace WebAppShield;

/// <summary>Maps the "monitor" setting to a real screen.</summary>
public static partial class MonitorHelper
{
    /// <summary>
    /// Screens in the order Windows numbers them in Settings &gt; Display, taken from the
    /// device name (\\.\DISPLAY1, \\.\DISPLAY2 ...). If a name cannot be parsed the
    /// screens are ordered left to right, then top to bottom.
    /// </summary>
    public static Screen[] Ordered()
    {
        var screens = Screen.AllScreens;
        if (screens.Length <= 1) return screens;

        bool allParsed = screens.All(s => ParseIndex(s.DeviceName) is not null);
        if (allParsed)
            return screens.OrderBy(s => ParseIndex(s.DeviceName)!.Value).ToArray();

        return screens.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).ToArray();
    }

    /// <summary>0 or an out of range number falls back to the primary screen.</summary>
    public static Screen Pick(int monitorNumber)
    {
        var primary = Screen.PrimaryScreen ?? Screen.AllScreens[0];
        if (monitorNumber <= 0) return primary;

        var ordered = Ordered();
        if (monitorNumber > ordered.Length) return primary;
        return ordered[monitorNumber - 1];
    }

    /// <summary>Number (1 based) of the screen that holds most of this rectangle. 0 if unknown.</summary>
    public static int NumberOf(Rectangle bounds)
    {
        var ordered = Ordered();
        var screen = Screen.FromRectangle(bounds);
        for (int i = 0; i < ordered.Length; i++)
            if (ordered[i].DeviceName == screen.DeviceName) return i + 1;
        return 0;
    }

    /// <summary>True when a decent part of the rectangle is on some connected screen.</summary>
    public static bool IsVisible(Rectangle bounds)
    {
        foreach (var screen in Screen.AllScreens)
        {
            var overlap = Rectangle.Intersect(screen.WorkingArea, bounds);
            if (overlap.Width >= Math.Min(120, bounds.Width) &&
                overlap.Height >= Math.Min(60, bounds.Height))
                return true;
        }
        return false;
    }

    /// <summary>Centre a size on a screen's working area, clamped so it stays on screen.</summary>
    public static Rectangle CenterOn(Screen screen, Size size)
    {
        var area = screen.WorkingArea;
        int w = Math.Min(size.Width, area.Width);
        int h = Math.Min(size.Height, area.Height);
        return new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
    }

    /// <summary>Push a rectangle back inside the nearest screen's working area.</summary>
    public static Rectangle Clamp(Rectangle bounds)
    {
        var area = Screen.FromRectangle(bounds).WorkingArea;
        int w = Math.Min(bounds.Width, area.Width);
        int h = Math.Min(bounds.Height, area.Height);
        int x = Math.Max(area.Left, Math.Min(bounds.X, area.Right - w));
        int y = Math.Max(area.Top, Math.Min(bounds.Y, area.Bottom - h));
        return new Rectangle(x, y, w, h);
    }

    private static int? ParseIndex(string deviceName)
    {
        var match = DisplayNumber().Match(deviceName ?? "");
        return match.Success && int.TryParse(match.Groups[1].Value, out int n) ? n : null;
    }

    [GeneratedRegex(@"DISPLAY(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex DisplayNumber();
}
