namespace Shield.SetIcon;

/// <summary>
/// Reads just enough of a .NET single file bundle to know where its file offsets
/// are stored. Every offset counts from the start of the exe, so when the appended
/// block moves, all of them have to move with it.
///
/// Layout, as written by Microsoft.NET.HostModel:
///     major (4) minor (4) file count (4) bundle id (text)
///     major >= 2: deps.json offset+size (8+8), runtimeconfig.json offset+size (8+8), flags (8)
///     then one entry per file:
///         offset (8) size (8) [major >= 6: compressed size (8)] type (1) path (text)
/// </summary>
internal static class BundleManifest
{
    private const uint HighestKnownMajorVersion = 6;

    /// <summary>Lists every place in the exe that holds an offset into the bundle.</summary>
    public static List<int> FindOffsetFields(byte[] bytes, long headerAt)
    {
        var fields = new List<int>();
        var reader = new Cursor(bytes, checked((int)headerAt));

        uint major = reader.UInt32();
        reader.UInt32();                       // minor version, not used
        int fileCount = reader.Int32();

        if (major is < 1 or > HighestKnownMajorVersion)
            throw new InvalidDataException(
                $"This exe uses single file bundle version {major}, which set-icon does not know. " +
                "Build the exe with the icon instead, or update set-icon.");

        reader.SkipString();                   // bundle id

        if (major >= 2)
        {
            AddOffsetField(fields, reader);    // deps.json
            reader.Int64();                    // its size
            AddOffsetField(fields, reader);    // runtimeconfig.json
            reader.Int64();                    // its size
            reader.Int64();                    // flags
        }

        for (int i = 0; i < fileCount; i++)
        {
            AddOffsetField(fields, reader);
            reader.Int64();                    // size
            if (major >= 6) reader.Int64();    // compressed size
            reader.Byte();                     // file type
            reader.SkipString();               // path inside the app
        }

        return fields;
    }

    // A zero offset means "this file is not there", and it must stay zero.
    private static void AddOffsetField(List<int> fields, Cursor reader)
    {
        int at = reader.Position;
        if (reader.Int64() != 0) fields.Add(at);
    }

    /// <summary>A tiny forward only reader; it throws rather than read past the end.</summary>
    private sealed class Cursor(byte[] bytes, int position)
    {
        public int Position { get; private set; } = position;

        public uint UInt32() => BitConverter.ToUInt32(bytes, Take(4));

        public int Int32() => BitConverter.ToInt32(bytes, Take(4));

        public long Int64() => BitConverter.ToInt64(bytes, Take(8));

        public byte Byte() => bytes[Take(1)];

        /// <summary>Text is a length, written 7 bits at a time, then the utf8 bytes.</summary>
        public void SkipString()
        {
            int length = 0;
            for (int shift = 0; ; shift += 7)
            {
                if (shift > 28) throw new InvalidDataException("The bundle holds a broken text length.");
                byte b = bytes[Take(1)];
                length |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
            }

            Take(length);
        }

        private int Take(int count)
        {
            if (count < 0 || Position + count > bytes.Length)
                throw new InvalidDataException("The single file bundle ends earlier than it says it does.");

            int at = Position;
            Position += count;
            return at;
        }
    }
}
