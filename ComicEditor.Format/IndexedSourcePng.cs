using System.Buffers.Binary;
using System.IO.Compression;

namespace ComicEditor.Format;

/// <summary>
/// Source-only indexed PNG: pixel values are project palette slots, not deduplicated colors.
/// Index 255 is transparent. The fixed palette preserves duplicate colors and unused slots.
/// </summary>
public static class IndexedSourcePng
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = CreateCrcTable();

    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> indices, IReadOnlyList<string> palette)
    {
        if (!ValidDimensions(width, height) || indices.Length != width * height)
            throw new ArgumentException("Invalid indexed image dimensions.");
        var colors = PaletteBytes(palette);
        ValidateIndices(indices, palette.Count);
        using var output = new MemoryStream(); output.Write(Signature);
        Span<byte> header = stackalloc byte[13]; header.Clear();
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8; header[9] = 3;
        Chunk(output, "IHDR"u8, header);
        Chunk(output, "PLTE"u8, colors);
        Span<byte> alpha = stackalloc byte[256]; alpha.Fill(255); alpha[255] = 0;
        Chunk(output, "tRNS"u8, alpha);
        using var compressed = new MemoryStream();
        using (var zip = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            // Filter 0 is inexpensive and particularly effective for sparse palette-index art.
            for (var y = 0; y < height; y++)
            {
                zip.WriteByte(0);
                zip.Write(indices.Slice(y * width, width));
            }
        }
        Chunk(output, "IDAT"u8, compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        Chunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    public static byte[] Decode(ReadOnlySpan<byte> png, int width, int height, IReadOnlyList<string> palette)
    {
        if (!ValidDimensions(width, height) || !png.StartsWith(Signature) || png.Length > (long)width * height * 2 + 1_048_576)
            throw new InvalidDataException("Invalid indexed source PNG.");
        var colors = PaletteBytes(palette);
        var offset = Signature.Length;
        var header = false; var hasPalette = false; var transparency = false; var image = false; var imageEnded = false; var end = false;
        using var data = new MemoryStream();
        while (offset <= png.Length - 12)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png[offset..]); offset += 4;
            if (length < 0 || length > png.Length - offset - 8) throw new InvalidDataException("Invalid indexed PNG chunk length.");
            var type = png.Slice(offset, 4); offset += 4;
            var payload = png.Slice(offset, length); offset += length;
            var expected = BinaryPrimitives.ReadUInt32BigEndian(png[offset..]); offset += 4;
            foreach (var c in type)
                if (!(c is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z')) throw new InvalidDataException("Invalid PNG chunk type.");
            if ((type[2] & 32) != 0 || Crc(type, payload) != expected) throw new InvalidDataException("Invalid indexed PNG chunk checksum or type.");
            if (!header && !type.SequenceEqual("IHDR"u8)) throw new InvalidDataException("PNG header must be first.");
            if (image && !type.SequenceEqual("IDAT"u8)) imageEnded = true;
            if (type.SequenceEqual("IHDR"u8))
            {
                if (header || length != 13 || BinaryPrimitives.ReadInt32BigEndian(payload) != width ||
                    BinaryPrimitives.ReadInt32BigEndian(payload[4..]) != height || payload[8] != 8 || payload[9] != 3 ||
                    payload[10] != 0 || payload[11] != 0 || payload[12] != 0)
                    throw new InvalidDataException("Unsupported indexed source PNG header.");
                header = true;
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                if (hasPalette || image || !payload.SequenceEqual(colors)) throw new InvalidDataException("Indexed PNG palette does not match project slots.");
                hasPalette = true;
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                if (!hasPalette || transparency || image || length != 256 || payload[255] != 0)
                    throw new InvalidDataException("Invalid indexed PNG transparency.");
                for (var i = 0; i < 255; i++)
                    if (payload[i] != 255) throw new InvalidDataException("Only index 255 may be transparent.");
                transparency = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (!hasPalette || !transparency || imageEnded) throw new InvalidDataException("Invalid indexed PNG image chunk order.");
                image = true; data.Write(payload);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (!image || length != 0) throw new InvalidDataException("Invalid indexed PNG end chunk.");
                end = true; break;
            }
            else if ((type[0] & 32) == 0) throw new InvalidDataException("Unsupported indexed PNG critical chunk.");
        }
        if (!header || !hasPalette || !transparency || !image || !end || offset != png.Length || data.Length < 6)
            throw new InvalidDataException("Incomplete indexed source PNG.");
        var compressed = data.GetBuffer().AsSpan(0, (int)data.Length);
        // Require a zlib wrapper (not a preset dictionary) and an intact final Adler checksum.
        if ((compressed[0] & 15) != 8 || compressed[0] >> 4 > 7 || (compressed[1] & 32) != 0 ||
            ((compressed[0] << 8) + compressed[1]) % 31 != 0) throw new InvalidDataException("Invalid indexed PNG zlib header.");
        var expectedAdler = BinaryPrimitives.ReadUInt32BigEndian(compressed[^4..]);
        data.Position = 0;
        using var zip = new ZLibStream(data, CompressionMode.Decompress);
        var pixels = new byte[width * height];
        var row = new byte[width + 1];
        uint adlerA = 1, adlerB = 0;
        try
        {
            for (var y = 0; y < height; y++)
            {
                zip.ReadExactly(row);
                // A scanline is at most 2049 bytes, below Adler's 5552-byte safe block limit.
                // Reduce once per row instead of dividing for every pixel.
                foreach (var value in row) { adlerA += value; adlerB += adlerA; }
                adlerA %= 65521; adlerB %= 65521;
                var filter = row[0];
                if (filter > 4) throw new InvalidDataException("Invalid indexed PNG row filter.");
                var start = y * width;
                if (filter == 0) { row.AsSpan(1).CopyTo(pixels.AsSpan(start, width)); continue; }
                for (var x = 0; x < width; x++)
                {
                    var left = x == 0 ? 0 : pixels[start + x - 1];
                    var above = y == 0 ? 0 : pixels[start + x - width];
                    var corner = x == 0 || y == 0 ? 0 : pixels[start + x - width - 1];
                    var predictor = filter switch { 0 => 0, 1 => left, 2 => above, 3 => (left + above) / 2, _ => Paeth(left, above, corner) };
                    pixels[start + x] = unchecked((byte)(row[x + 1] + predictor));
                }
            }
            if (zip.ReadByte() != -1) throw new InvalidDataException("Indexed PNG contains extra pixels.");
        }
        catch (EndOfStreamException ex) { throw new InvalidDataException("Truncated indexed PNG pixels.", ex); }
        if ((adlerB << 16 | adlerA) != expectedAdler) throw new InvalidDataException("Invalid indexed PNG zlib checksum.");
        ValidateIndices(pixels, palette.Count);
        return pixels;
    }

    private static bool ValidDimensions(int width, int height) => width is >= 1 and <= 2048 && height is >= 1 and <= 2048;

    private static byte[] PaletteBytes(IReadOnlyList<string> palette)
    {
        if (palette.Count is < 2 or > 255) throw new InvalidDataException("Invalid indexed source palette size.");
        var colors = new byte[256 * 3];
        for (var i = 0; i < palette.Count; i++)
        {
            var color = palette[i];
            if (color.Length != 7 || color[0] != '#' || !uint.TryParse(color.AsSpan(1), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var rgb)) throw new InvalidDataException("Invalid indexed source palette color.");
            colors[i * 3] = (byte)(rgb >> 16); colors[i * 3 + 1] = (byte)(rgb >> 8); colors[i * 3 + 2] = (byte)rgb;
        }
        return colors;
    }

    private static void ValidateIndices(ReadOnlySpan<byte> indices, int count)
    {
        foreach (var index in indices)
            if (index != 255 && index >= count) throw new InvalidDataException("Indexed PNG pixel exceeds the project palette.");
    }

    private static int Paeth(int left, int above, int corner)
    {
        var p = left + above - corner;
        var a = Math.Abs(p - left); var b = Math.Abs(p - above); var c = Math.Abs(p - corner);
        return a <= b && a <= c ? left : b <= c ? above : corner;
    }

    private static void Chunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> payload)
    {
        Span<byte> word = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(word, payload.Length);
        output.Write(word); output.Write(type); output.Write(payload);
        BinaryPrimitives.WriteUInt32BigEndian(word, Crc(type, payload)); output.Write(word);
    }

    private static uint Crc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> payload)
    {
        var crc = uint.MaxValue;
        foreach (var value in type) crc = crc >> 8 ^ CrcTable[(crc ^ value) & 255];
        foreach (var value in payload) crc = crc >> 8 ^ CrcTable[(crc ^ value) & 255];
        return ~crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var crc = i;
            for (var bit = 0; bit < 8; bit++) crc = crc >> 1 ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
            table[i] = crc;
        }
        return table;
    }
}
