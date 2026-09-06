using System.Runtime.InteropServices;

namespace WebAppShield;

internal static class NativeMethods
{
    // ---- window messages / hit test --------------------------------------
    public const int WM_NCHITTEST = 0x0084;
    public const int WM_SYSCOMMAND = 0x0112;
    public const int WM_QUERYENDSESSION = 0x0011;
    public const int WM_ENDSESSION = 0x0016;

    public const int SC_MINIMIZE = 0xF020;
    public const int SC_MAXIMIZE = 0xF030;
    public const int SC_RESTORE = 0xF120;
    public const int SC_CLOSE = 0xF060;

    public const int HTCLIENT = 1;
    public const int HTLEFT = 10;
    public const int HTRIGHT = 11;
    public const int HTTOP = 12;
    public const int HTTOPLEFT = 13;
    public const int HTTOPRIGHT = 14;
    public const int HTBOTTOM = 15;
    public const int HTBOTTOMLEFT = 16;
    public const int HTBOTTOMRIGHT = 17;

    /// <summary>Class style that makes Windows draw a greyed out, disabled X button.</summary>
    public const int CS_NOCLOSE = 0x0200;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    // ---- system menu (used to remove Close when the close tag is absent) --
    [DllImport("user32.dll")]
    public static extern IntPtr GetSystemMenu(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool bRevert);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnableMenuItem(IntPtr hMenu, uint uIDEnableItem, uint uEnable);

    public const uint MF_BYCOMMAND = 0x0000;
    public const uint MF_GRAYED = 0x0001;

    // ---- memory ----------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll")]
    public static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr min, IntPtr max);

    /// <summary>Free physical memory as a fraction between 0 and 1. Returns 1 if unknown.</summary>
    public static double GetFreeMemoryFraction()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0) return 1.0;
        return (double)status.ullAvailPhys / status.ullTotalPhys;
    }

    /// <summary>Ask Windows to trim this process's working set back to disk.</summary>
    public static void TrimWorkingSet()
    {
        try { SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1)); }
        catch { /* never important enough to fail on */ }
    }
}
