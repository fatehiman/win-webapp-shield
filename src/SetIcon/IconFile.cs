namespace Shield.SetIcon;

/// <summary>One image inside an .ico file.</summary>
internal sealed class IconImage
{
    public byte Width;        // 0 means 256
    public byte Height;       // 0 means 256
    public byte ColorCount;
    public byte Reserved;
    public ushort Planes;
    public ushort BitCount;
    public byte[] Data = [];

    public string Describe()
    {
        int w = Width == 0 ? 256 : Width;
        int h = Height == 0 ? 256 : Height;
        string kind = Data.Length > 8 && Data[0] == 0x89 && Data[1] == (byte)'P' ? "PNG" : "BMP";
        return $"{w}x{h} {BitCount}-bit {kind}";
    }
}

/// <summary>Reads the images out of an .ico file.</summary>
internal static class IconFile
{
    public static List<IconImage> Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 6) throw new InvalidDataException("The file is too small to be an .ico file.");

        ushort reserved = BitConverter.ToUInt16(bytes, 0);
        ushort type = BitConverter.ToUInt16(bytes, 2);
        ushort count = BitConverter.ToUInt16(bytes, 4);

        if (reserved != 0 || type != 1)
            throw new InvalidDataException("This is not an icon file. A .cur cursor file will not do.");
        if (count == 0)
            throw new InvalidDataException("The icon file holds no images.");
        if (bytes.Length < 6 + count * 16)
            throw new InvalidDataException("The icon directory is cut short.");

        var images = new List<IconImage>(count);
        for (int i = 0; i < count; i++)
        {
            int at = 6 + i * 16;
            int size = BitConverter.ToInt32(bytes, at + 8);
            int offset = BitConverter.ToInt32(bytes, at + 12);

            if (size <= 0 || offset < 0 || (long)offset + size > bytes.Length)
                throw new InvalidDataException($"Image {i + 1} points outside the file.");

            var image = new IconImage
            {
                Width = bytes[at + 0],
                Height = bytes[at + 1],
                ColorCount = bytes[at + 2],
                Reserved = bytes[at + 3],
                Planes = BitConverter.ToUInt16(bytes, at + 4),
                BitCount = BitConverter.ToUInt16(bytes, at + 6),
                Data = bytes.AsSpan(offset, size).ToArray(),
            };

            // Some tools leave these two fields at zero in the directory. The real
            // values are in the DIB header of the image itself, and Windows shows a
            // black icon without them, so fill them in.
            if (image.Planes == 0 && image.BitCount == 0 && image.Data.Length >= 16 &&
                BitConverter.ToInt32(image.Data, 0) == 40)
            {
                image.Planes = BitConverter.ToUInt16(image.Data, 12);
                image.BitCount = BitConverter.ToUInt16(image.Data, 14);
            }

            images.Add(image);
        }

        return images;
    }

    /// <summary>
    /// Builds the RT_GROUP_ICON block: the same header as the file, but every entry
    /// ends in the resource id of its image instead of a file offset.
    /// </summary>
    public static byte[] BuildGroupDirectory(IReadOnlyList<IconImage> images, ushort firstIconId)
    {
        byte[] block = new byte[6 + images.Count * 14];
        BitConverter.GetBytes((ushort)0).CopyTo(block, 0);
        BitConverter.GetBytes((ushort)1).CopyTo(block, 2);
        BitConverter.GetBytes((ushort)images.Count).CopyTo(block, 4);

        for (int i = 0; i < images.Count; i++)
        {
            int at = 6 + i * 14;
            IconImage image = images[i];
            block[at + 0] = image.Width;
            block[at + 1] = image.Height;
            block[at + 2] = image.ColorCount;
            block[at + 3] = image.Reserved;
            BitConverter.GetBytes(image.Planes).CopyTo(block, at + 4);
            BitConverter.GetBytes(image.BitCount).CopyTo(block, at + 6);
            BitConverter.GetBytes(image.Data.Length).CopyTo(block, at + 8);
            BitConverter.GetBytes((ushort)(firstIconId + i)).CopyTo(block, at + 12);
        }

        return block;
    }
}
