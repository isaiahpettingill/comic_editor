using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace ComicEditor.Format;

/// <summary>Opaque indexed PNG with a compact palette of exactly the RGB colors present.</summary>
public static class IndexedPng
{
    public static void Write(Stream output, int width, int height, ReadOnlySpan<uint> rgb)
    {
        if (width <= 0 || height <= 0 || (long)width * height != rgb.Length) throw new ArgumentException("Invalid image dimensions.");
        var palette = new Dictionary<uint, byte>(); var indices = new byte[rgb.Length];
        for (var i = 0; i < rgb.Length; i++)
        {
            var color = rgb[i] & 0xffffff;
            if (!palette.TryGetValue(color, out var index))
            {
                if (palette.Count == 256) throw new ArgumentException("Indexed PNG supports at most 256 distinct colors.");
                palette.Add(color, index = (byte)palette.Count);
            }
            indices[i] = index;
        }
        var depth = palette.Count <= 2 ? 1 : palette.Count <= 4 ? 2 : palette.Count <= 16 ? 4 : 8;
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = (byte)depth; header[9] = 3; Chunk(output, "IHDR", header);
        var colors = new byte[palette.Count * 3];
        foreach (var (color, index) in palette) { colors[index * 3] = (byte)(color >> 16); colors[index * 3 + 1] = (byte)(color >> 8); colors[index * 3 + 2] = (byte)color; }
        Chunk(output, "PLTE", colors);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var row = new byte[1 + (width * depth + 7) / 8]; // Filter 0, then packed pixels, high bits first.
            for (var y = 0; y < height; y++)
            {
                Array.Clear(row);
                for (var x = 0; x < width; x++) row[1 + x * depth / 8] |= (byte)(indices[y * width + x] << (8 - depth - x * depth % 8));
                zlib.Write(row);
            }
        }
        Chunk(output, "IDAT", compressed.ToArray()); Chunk(output, "IEND", []);
    }

    private static void Chunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(word, data.Length); output.Write(word);
        var name = Encoding.ASCII.GetBytes(type); output.Write(name); output.Write(data);
        var crc = uint.MaxValue;
        foreach (var value in name) crc = CrcByte(crc, value);
        foreach (var value in data) crc = CrcByte(crc, value);
        BinaryPrimitives.WriteUInt32BigEndian(word, ~crc); output.Write(word);
    }
    private static uint CrcByte(uint crc, byte value)
    {
        crc ^= value;
        for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        return crc;
    }
}
