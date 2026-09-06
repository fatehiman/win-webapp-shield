using System.Runtime.InteropServices;

namespace Shield.SetIcon;

/// <summary>
/// The Win32 resource update API. Windows rewrites the PE resource directory for
/// us, which is why an already built exe can be given a new icon without a rebuild.
/// </summary>
internal static class NativeMethods
{
    internal const int RT_ICON = 3;
    internal const int RT_GROUP_ICON = 14;

    internal const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;
    internal const uint LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x00000020;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr BeginUpdateResourceW(string pFileName,
        [MarshalAs(UnmanagedType.Bool)] bool bDeleteExistingResources);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UpdateResourceW(IntPtr hUpdate, IntPtr lpType, IntPtr lpName,
        ushort wLanguage, byte[]? lpData, uint cb);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EndUpdateResourceW(IntPtr hUpdate,
        [MarshalAs(UnmanagedType.Bool)] bool fDiscard);

    /// <summary>Tells us whether this console belongs to us alone, i.e. Explorer started it.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint GetConsoleProcessList(uint[] lpdwProcessList, uint dwProcessCount);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr LoadLibraryExW(string lpLibFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FreeLibrary(IntPtr hModule);

    internal delegate bool EnumResNameProc(IntPtr hModule, IntPtr lpType, IntPtr lpName, IntPtr lParam);

    internal delegate bool EnumResLangProc(IntPtr hModule, IntPtr lpType, IntPtr lpName, ushort wLang, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumResourceNamesW(IntPtr hModule, IntPtr lpType,
        EnumResNameProc lpEnumFunc, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumResourceLanguagesW(IntPtr hModule, IntPtr lpType, IntPtr lpName,
        EnumResLangProc lpEnumFunc, IntPtr lParam);
}
