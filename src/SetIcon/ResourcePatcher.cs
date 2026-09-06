using System.ComponentModel;
using System.Runtime.InteropServices;
using static Shield.SetIcon.NativeMethods;

namespace Shield.SetIcon;

/// <summary>A resource name, which Windows allows to be either a number or a string.</summary>
internal readonly struct ResName(ushort id, string? name, ushort language)
{
    public readonly ushort Id = id;
    public readonly string? Name = name;
    public readonly ushort Language = language;

    public bool IsId => Name is null;

    public override string ToString() => Name ?? "#" + Id;
}

internal static class ResourcePatcher
{
    /// <summary>
    /// Replaces every icon in the exe with the images of the given .ico file.
    /// A copy is kept next to it until the whole job has gone through.
    /// </summary>
    public static void SetIcon(string exePath, IReadOnlyList<IconImage> images)
    {
        // Windows will not let a running program be rewritten, and saying so here is
        // clearer than the copy below failing.
        try
        {
            using var probe = new FileStream(exePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            throw new IOException(
                $"{Path.GetFileName(exePath)} is in use. Close the app, and anything looking at the file, then try again.");
        }

        string backup = exePath + ".set-icon-backup";
        File.Copy(exePath, backup, true);

        try
        {
            SingleFileBundle? bundle = SingleFileBundle.Detach(exePath);
            try
            {
                PatchResources(exePath, images);
            }
            catch
            {
                bundle?.Restore(exePath);
                throw;
            }

            bundle?.Reattach(exePath);
        }
        catch
        {
            File.Copy(backup, exePath, true);
            File.Delete(backup);
            throw;
        }

        File.Delete(backup);
    }

    private static void PatchResources(string exePath, IReadOnlyList<IconImage> images)
    {
        List<ResName> oldIcons = ListResources(exePath, RT_ICON);
        List<ResName> oldGroups = ListResources(exePath, RT_GROUP_ICON);

        // Explorer shows the icon group with the lowest id, so the new one takes the
        // place of the old one instead of sitting next to it.
        ushort groupId = 1;
        ushort language = 0;
        foreach (ResName group in oldGroups)
        {
            if (group.IsId && (language == 0 && groupId == 1 || group.Id < groupId))
            {
                groupId = group.Id;
                language = group.Language;
            }
        }

        IntPtr handle = BeginUpdateResourceW(exePath, false);
        if (handle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"Windows would not open {Path.GetFileName(exePath)} for editing. Close the app if it is running.");

        bool committed = false;
        try
        {
            foreach (ResName old in oldIcons) Delete(handle, RT_ICON, old);
            foreach (ResName old in oldGroups) Delete(handle, RT_GROUP_ICON, old);

            for (int i = 0; i < images.Count; i++)
                Write(handle, RT_ICON, (ushort)(1 + i), language, images[i].Data);

            Write(handle, RT_GROUP_ICON, groupId, language, IconFile.BuildGroupDirectory(images, 1));

            if (!EndUpdateResourceW(handle, false))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Writing the new icon back to the exe failed.");

            committed = true;
        }
        finally
        {
            if (!committed) EndUpdateResourceW(handle, true);
        }
    }

    private static void Delete(IntPtr handle, int type, ResName name)
    {
        bool ok = name.IsId
            ? UpdateResourceW(handle, type, name.Id, name.Language, null, 0)
            : WithStringName(name.Name!, ptr => UpdateResourceW(handle, type, ptr, name.Language, null, 0));

        if (!ok)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Removing the old icon resource {name} failed.");
    }

    private static void Write(IntPtr handle, int type, ushort id, ushort language, byte[] data)
    {
        if (!UpdateResourceW(handle, type, id, language, data, (uint)data.Length))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Writing icon resource #{id} failed.");
    }

    private static bool WithStringName(string name, Func<IntPtr, bool> action)
    {
        IntPtr ptr = Marshal.StringToHGlobalUni(name);
        try { return action(ptr); }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    /// <summary>Lists the resources of one type already in the file, with their languages.</summary>
    private static List<ResName> ListResources(string exePath, int type)
    {
        var found = new List<ResName>();

        IntPtr module = LoadLibraryExW(exePath, IntPtr.Zero,
            LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE);
        if (module == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"{Path.GetFileName(exePath)} could not be read as a Windows program.");

        try
        {
            EnumResNameProc onName = (m, t, namePtr, _) =>
            {
                EnumResLangProc onLang = (_, _, _, lang, _) =>
                {
                    found.Add(IsIntResource(namePtr)
                        ? new ResName((ushort)namePtr, null, lang)
                        : new ResName(0, Marshal.PtrToStringUni(namePtr), lang));
                    return true;
                };

                EnumResourceLanguagesW(m, t, namePtr, onLang, IntPtr.Zero);
                GC.KeepAlive(onLang);
                return true;
            };

            // No resources of this type at all is normal, not an error.
            EnumResourceNamesW(module, type, onName, IntPtr.Zero);
            GC.KeepAlive(onName);
        }
        finally
        {
            FreeLibrary(module);
        }

        return found;
    }

    private static bool IsIntResource(IntPtr value) => (ulong)value <= ushort.MaxValue;
}
