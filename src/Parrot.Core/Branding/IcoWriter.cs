namespace Parrot.Core.Branding;

/// <summary>Packs PNG frames into an .ico file.</summary>
public static class IcoWriter
{
    /// <summary>
    /// Windows has accepted PNG-compressed icon frames since Vista, which keeps the large
    /// frames small. Frames are written smallest first.
    /// </summary>
    public static byte[] Build(IReadOnlyDictionary<int, byte[]> pngFramesBySize)
    {
        var ordered = pngFramesBySize.OrderBy(f => f.Key).ToList();

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((ushort)0);                 // reserved
        writer.Write((ushort)1);                 // type: icon
        writer.Write((ushort)ordered.Count);

        var offset = 6 + ordered.Count * 16;

        foreach (var (size, png) in ordered)
        {
            writer.Write((byte)(size >= 256 ? 0 : size)); // 0 means 256
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);               // palette size
            writer.Write((byte)0);               // reserved
            writer.Write((ushort)1);             // colour planes
            writer.Write((ushort)32);            // bits per pixel
            writer.Write(png.Length);
            writer.Write(offset);

            offset += png.Length;
        }

        foreach (var (_, png) in ordered)
            writer.Write(png);

        writer.Flush();
        return stream.ToArray();
    }
}
