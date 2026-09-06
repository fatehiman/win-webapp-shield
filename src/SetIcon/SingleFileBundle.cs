namespace Shield.SetIcon;

/// <summary>
/// A .NET single file exe is a small native program with the whole app appended
/// after the end of the last PE section, plus one 8 byte number inside the program
/// that says where that appended block starts.
///
/// Windows rewrites the PE when it stores the new icon, which moves the end of the
/// file, and the app would no longer be found: "Failure processing application
/// bundle". So the block is taken off before the icon is written and put back after,
/// with that number corrected.
/// </summary>
internal sealed class SingleFileBundle
{
    private readonly byte[] _payload;
    private readonly long _markerValue;   // where the bundle header sits today
    private readonly int _markerAt;       // where that number is stored in the exe
    private readonly long _payloadStart;
    private readonly List<int> _offsetFields;   // inside the block, counted from its start

    private SingleFileBundle(byte[] payload, long markerValue, int markerAt, long payloadStart,
        List<int> offsetFields)
    {
        _payload = payload;
        _markerValue = markerValue;
        _markerAt = markerAt;
        _payloadStart = payloadStart;
        _offsetFields = offsetFields;
    }

    public int PayloadLength => _payload.Length;

    /// <summary>
    /// Cuts the appended block off the exe, or returns null if the exe has none.
    /// </summary>
    public static SingleFileBundle? Detach(string exePath)
    {
        byte[] bytes = File.ReadAllBytes(exePath);
        long peEnd = EndOfSections(bytes);

        if (peEnd <= 0 || peEnd >= bytes.Length) return null;   // nothing appended

        int markerAt = FindMarker(bytes, peEnd);
        long markerValue = BitConverter.ToInt64(bytes, markerAt);

        // Read the list of offsets before anything is written, so a bundle we cannot
        // make sense of stops the job while the exe is still whole.
        List<int> offsetFields = BundleManifest.FindOffsetFields(bytes, markerValue)
            .ConvertAll(at => at - (int)peEnd);

        byte[] payload = bytes.AsSpan((int)peEnd).ToArray();

        using (var file = new FileStream(exePath, FileMode.Open, FileAccess.Write, FileShare.None))
            file.SetLength(peEnd);

        return new SingleFileBundle(payload, markerValue, markerAt, peEnd, offsetFields);
    }

    /// <summary>
    /// Puts the block back at the new end of the file. Every offset it holds counts
    /// from the start of the exe, so they all move by the same amount.
    /// </summary>
    public void Reattach(string exePath)
    {
        byte[] stub = File.ReadAllBytes(exePath);
        long delta = stub.Length - _payloadStart;

        int at = LocateMarkerAgain(stub);
        BitConverter.GetBytes(_markerValue + delta).CopyTo(stub, at);

        foreach (int field in _offsetFields)
            BitConverter.GetBytes(BitConverter.ToInt64(_payload, field) + delta).CopyTo(_payload, field);

        using var file = new FileStream(exePath, FileMode.Create, FileAccess.Write, FileShare.None);
        file.Write(stub);
        file.Write(_payload);
    }

    /// <summary>Writes the file back exactly as it was, used when something went wrong.</summary>
    public void Restore(string exePath)
    {
        using var file = new FileStream(exePath, FileMode.Open, FileAccess.Write, FileShare.None);
        file.Seek(0, SeekOrigin.End);
        file.Write(_payload);
    }

    /// <summary>The end of the last section, i.e. where anything appended must start.</summary>
    private static long EndOfSections(byte[] bytes)
    {
        if (bytes.Length < 0x40 || bytes[0] != 'M' || bytes[1] != 'Z') return -1;

        int pe = BitConverter.ToInt32(bytes, 0x3C);
        if (pe <= 0 || pe + 24 > bytes.Length) return -1;
        if (BitConverter.ToUInt32(bytes, pe) != 0x00004550) return -1;   // "PE\0\0"

        int sectionCount = BitConverter.ToUInt16(bytes, pe + 6);
        int optionalSize = BitConverter.ToUInt16(bytes, pe + 20);
        int table = pe + 24 + optionalSize;

        long end = 0;
        for (int i = 0; i < sectionCount; i++)
        {
            int at = table + i * 40;
            if (at + 40 > bytes.Length) return -1;

            long rawSize = BitConverter.ToUInt32(bytes, at + 16);
            long rawStart = BitConverter.ToUInt32(bytes, at + 20);
            if (rawStart == 0 || rawSize == 0) continue;

            end = Math.Max(end, rawStart + rawSize);
        }

        return end;
    }

    /// <summary>
    /// Finds where the 8 byte number that points at the appended block is stored.
    /// A number alone is not proof, so every candidate has to point at something
    /// that really reads like a .NET bundle header.
    /// </summary>
    private static int FindMarker(byte[] bytes, long payloadStart)
    {
        int found = -1;
        for (int i = 0; i + 8 <= payloadStart; i++)
        {
            long value = BitConverter.ToInt64(bytes, i);
            if (value < payloadStart || value >= bytes.Length) continue;
            if (!LooksLikeBundleHeader(bytes, value)) continue;

            if (found >= 0 && BitConverter.ToInt64(bytes, found) != value)
                throw new InvalidDataException(
                    "This exe has more than one thing that looks like a single file marker. " +
                    "set-icon will not touch it, to avoid breaking it.");

            found = i;
        }

        if (found < 0)
            throw new InvalidDataException(
                "This exe has data after the last section, but no .NET single file marker pointing at it. " +
                "It may be code signed, or packed by another tool. set-icon will not touch it, to avoid breaking it.");

        return found;
    }

    /// <summary>
    /// The bundle header is: version numbers, how many files are inside, then the
    /// bundle id as a short text. Junk almost never passes all of that.
    /// </summary>
    private static bool LooksLikeBundleHeader(byte[] bytes, long at)
    {
        if (at + 13 > bytes.Length) return false;

        uint major = BitConverter.ToUInt32(bytes, (int)at);
        uint minor = BitConverter.ToUInt32(bytes, (int)at + 4);
        int fileCount = BitConverter.ToInt32(bytes, (int)at + 8);

        if (major is < 1 or > 9 || minor > 9) return false;
        if (fileCount is < 1 or > 100_000) return false;

        int idLength = bytes[(int)at + 12];          // 7 bit encoded length, one byte is plenty
        if (idLength is < 1 or > 127) return false;
        if (at + 13 + idLength > bytes.Length) return false;

        for (int i = 0; i < idLength; i++)
        {
            byte c = bytes[(int)at + 13 + i];
            if (c is < 0x20 or > 0x7E) return false;
        }

        return true;
    }

    /// <summary>
    /// Finds the number again after Windows has rewritten the exe. It normally sits
    /// where it did, because only the resource part moves.
    /// </summary>
    private int LocateMarkerAgain(byte[] stub)
    {
        if (_markerAt + 8 <= stub.Length && BitConverter.ToInt64(stub, _markerAt) == _markerValue)
            return _markerAt;

        int at = -1;
        for (int i = 0; i + 8 <= stub.Length; i++)
        {
            if (BitConverter.ToInt64(stub, i) != _markerValue) continue;
            if (at >= 0)
                throw new InvalidDataException(
                    "The single file marker moved and cannot be told apart any more. The exe was left unchanged.");
            at = i;
        }

        if (at < 0)
            throw new InvalidDataException(
                "The single file marker disappeared while the icon was written. The exe was left unchanged.");

        return at;
    }

}
