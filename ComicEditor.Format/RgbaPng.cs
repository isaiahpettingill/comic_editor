using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace ComicEditor.Format;

/// <summary>Lossless PNG color type 6. The decoder accepts the filter-0 PNGs produced here.</summary>
public static class RgbaPng
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    public static byte[] Encode(int width, int height, ReadOnlySpan<uint> rgba)
    {
        if (width is < 1 or > 2048 || height is < 1 or > 2048 || rgba.Length != width * height)
            throw new ArgumentException("Invalid RGBA image dimensions.");
        using var output = new MemoryStream(); output.Write(Signature);
        var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 6; Chunk(output, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zip = new ZLibStream(compressed, CompressionLevel.Fastest, true))
        {
            var row = new byte[1 + width * 4];
            for (var y = 0; y < height; y++)
            {
                row[0] = 0;
                for (var x = 0; x < width; x++) BinaryPrimitives.WriteUInt32BigEndian(row.AsSpan(1 + x * 4), rgba[y * width + x]);
                zip.Write(row);
            }
        }
        Chunk(output, "IDAT", compressed.ToArray()); Chunk(output, "IEND", []);
        return output.ToArray();
    }
    public static uint[] Decode(ReadOnlySpan<byte> png, int width, int height)
    {
        if (width is < 1 or > 2048 || height is < 1 or > 2048 || !png.StartsWith(Signature)) throw new InvalidDataException("Invalid RGBA PNG.");
        var offset = Signature.Length; var header = false; var end = false;
        using var data = new MemoryStream();
        while (offset + 12 <= png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png[offset..]); offset += 4;
            if (length < 0 || length > png.Length - offset - 8) throw new InvalidDataException("Invalid PNG chunk length.");
            var type = png.Slice(offset, 4); offset += 4;
            var payload = png.Slice(offset, length); offset += length;
            var expected = BinaryPrimitives.ReadUInt32BigEndian(png[offset..]); offset += 4;
            var crc = uint.MaxValue;
            foreach (var value in type) crc = CrcByte(crc, value);
            foreach (var value in payload) crc = CrcByte(crc, value);
            if (~crc != expected) throw new InvalidDataException("Invalid PNG checksum.");
            if (type.SequenceEqual("IHDR"u8))
            {
                if (header || length != 13 || BinaryPrimitives.ReadInt32BigEndian(payload) != width || BinaryPrimitives.ReadInt32BigEndian(payload[4..]) != height ||
                    payload[8] != 8 || payload[9] != 6 || payload[10] != 0 || payload[11] != 0 || payload[12] != 0) throw new InvalidDataException("Unsupported RGBA PNG header.");
                header = true;
            }
            else if (type.SequenceEqual("IDAT"u8)) { if (!header) throw new InvalidDataException("PNG lacks a header."); data.Write(payload); }
            else if (type.SequenceEqual("IEND"u8)) { end = true; break; }
            else if ((type[0] & 32) == 0) throw new InvalidDataException("Unsupported PNG chunk.");
        }
        if (!header || !end || offset != png.Length) throw new InvalidDataException("Incomplete RGBA PNG.");
        data.Position = 0; using var zip = new ZLibStream(data, CompressionMode.Decompress);
        var row = new byte[1 + width * 4]; var pixels = new uint[width * height];
        for (var y = 0; y < height; y++)
        {
            zip.ReadExactly(row);
            if (row[0] != 0) throw new InvalidDataException("Unsupported PNG filter.");
            for (var x = 0; x < width; x++) pixels[y * width + x] = BinaryPrimitives.ReadUInt32BigEndian(row.AsSpan(1 + x * 4));
        }
        if (zip.ReadByte() != -1) throw new InvalidDataException("RGBA PNG has extra pixels.");
        return pixels;
    }
    private static void Chunk(Stream output, string name, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(word, data.Length); output.Write(word);
        var type = Encoding.ASCII.GetBytes(name); output.Write(type); output.Write(data);
        var crc = uint.MaxValue;
        foreach (var value in type) crc = CrcByte(crc, value);
        foreach (var value in data) crc = CrcByte(crc, value);
        BinaryPrimitives.WriteUInt32BigEndian(word, ~crc); output.Write(word);
    }
    private static uint CrcByte(uint crc, byte value)
    {
        crc ^= value;
        for (var i = 0; i < 8; i++) crc = crc >> 1 ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
        return crc;
    }
}
