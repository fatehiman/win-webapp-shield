using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WebAppShield;

/// <summary>
/// Draws the small "this app is asleep" mark on top of the tray icon.
///
/// A sleeping app is still in the tray, but its browser has been dropped to give the
/// memory back to Windows. The icon on its own cannot show that, so a gray dot is
/// painted in the top right corner. The dot has a thin light ring around it so it
/// stays readable on a dark taskbar and on a light one.
/// </summary>
public static class TrayBadge
{
    /// <summary>Dot width as a fraction of the icon width.</summary>
    private const float DotFraction = 0.36f;

    private static readonly Color DotFill = Color.FromArgb(255, 150, 150, 150);
    private static readonly Color DotEdge = Color.FromArgb(255, 90, 90, 90);
    private static readonly Color DotRing = Color.FromArgb(230, 245, 245, 245);

    /// <summary>
    /// Returns a copy of <paramref name="source"/> with the gray dot on it, or null if
    /// the copy could not be made. The caller owns the returned icon and must dispose it.
    /// </summary>
    public static Icon? WithSleepDot(Icon source)
    {
        try
        {
            var size = SystemInformation.SmallIconSize;
            if (size.Width < 8 || size.Height < 8) size = new Size(16, 16);

            using var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                // Ask the .ico for the frame closest to the tray size, then paint it.
                using (var frame = new Icon(source, size))
                    g.DrawImage(frame.ToBitmap(), new Rectangle(0, 0, size.Width, size.Height));

                DrawDot(g, size);
            }

            // GetHicon hands out a handle we have to free ourselves. Clone() copies the
            // bits into a managed icon, so the handle can go straight back.
            IntPtr handle = bmp.GetHicon();
            try
            {
                using var temp = Icon.FromHandle(handle);
                return (Icon)temp.Clone();
            }
            finally
            {
                NativeMethods.DestroyIcon(handle);
            }
        }
        catch
        {
            return null;   // a badge is a nicety, never a reason to break the tray
        }
    }

    private static void DrawDot(Graphics g, Size size)
    {
        float d = Math.Max(5f, size.Width * DotFraction);
        float x = size.Width - d - 0.5f;
        float y = 0.5f;
        var dot = new RectangleF(x, y, d, d);

        // Light ring first, so the dot keeps its shape over a busy or dark icon.
        using (var ring = new Pen(DotRing, Math.Max(1f, d * 0.16f)))
            g.DrawEllipse(ring, dot);

        using (var fill = new SolidBrush(DotFill))
            g.FillEllipse(fill, dot);

        using (var edge = new Pen(DotEdge, Math.Max(1f, d * 0.10f)))
            g.DrawEllipse(edge, dot);
    }
}
