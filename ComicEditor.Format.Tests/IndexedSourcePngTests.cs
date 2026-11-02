using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using ComicEditor.Format;

namespace ComicEditor.Format.Tests;

public sealed class IndexedSourcePngTests
{
    private static readonly string[] Palette = ["#112233", "#ABCDEF", "#112233"];
    private static byte[] Encode(byte[] pixels, int width, int height) => IndexedSourcePng.Encode(width, height, pixels, Palette);
    private static byte[] Decode(byte[] png, int width, int height) => IndexedSourcePng.Decode(png, width, height, Palette);

    [Fact]
    public void DuplicateColorsTransparentIndexAndUnusedPaletteSlotsArePreserved()
    {
        byte[] pixels = [0, 1, 2, 255, 2, 0, 255, 1, 0];
        var png = Encode(pixels, 3, 3);
        Assert.Equal(pixels, Decode(png, 3, 3));
        var chunks = Chunks(png);
        var colors = chunks.Single(c => c.Name == "PLTE").Data;
        Assert.Equal(768, colors.Length);
        Assert.Equal(colors[..3], colors[6..9]);
        Assert.All(colors[9..], b => Assert.Equal(0, b));
        var alpha = chunks.Single(c => c.Name == "tRNS").Data;
        Assert.Equal(256, alpha.Length);
        Assert.All(alpha[..255], b => Assert.Equal(255, b));
        Assert.Equal(0, alpha[255]);
    }

    [Fact]
    public void EveryPaletteSlotIncluding254RoundTrips()
    {
        var palette = Enumerable.Range(0, 255).Select(i => $"#{i:X2}{i:X2}{i:X2}").ToArray();
        var pixels = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        Assert.Equal(pixels, IndexedSourcePng.Decode(IndexedSourcePng.Encode(256, 1, pixels, palette), 256, 1, palette));
    }

    [Theory]
    [InlineData(1, 1)] [InlineData(1, 2048)] [InlineData(2048, 1)]
    [InlineData(3, 7)] [InlineData(67, 3)] [InlineData(129, 13)]
    public void OddDimensionsAndSparseOrDensePixelsRoundTrip(int width, int height)
    {
        var random = new Random(913);
        var pixels = Enumerable.Range(0, width * height).Select(_ => new byte[] { 0, 1, 2, 255 }[random.Next(4)]).ToArray();
        Assert.Equal(pixels, Decode(Encode(pixels, width, height), width, height));
        Array.Fill(pixels, (byte)255);
        Assert.Equal(pixels, Decode(Encode(pixels, width, height), width, height));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void IndependentlyFilteredRowsDecodeExactly(int filter)
    {
        const int width = 67, height = 11;
        var random = new Random(17);
        var pixels = Enumerable.Range(0, width * height).Select(_ => new byte[] { 0, 1, 2, 255 }[random.Next(4)]).ToArray();
        var rows = new byte[(width + 1) * height];
        for (var y = 0; y < height; y++)
        {
            rows[y * (width + 1)] = (byte)filter;
            for (var x = 0; x < width; x++)
            {
                var left = x == 0 ? 0 : pixels[y * width + x - 1];
                var above = y == 0 ? 0 : pixels[(y - 1) * width + x];
                var corner = x == 0 || y == 0 ? 0 : pixels[(y - 1) * width + x - 1];
                var predictor = filter switch { 0 => 0, 1 => left, 2 => above, 3 => (left + above) / 2, _ => Paeth(left, above, corner) };
                rows[y * (width + 1) + x + 1] = unchecked((byte)(pixels[y * width + x] - predictor));
            }
        }
        Assert.Equal(pixels, Decode(Replace(Encode(pixels, width, height), "IDAT", Compress(rows)), width, height));
    }

    [Fact]
    public void EveryTruncationSingleByteCorruptionAndTrailingByteIsRejected()
    {
        var png = Encode([0, 1, 2, 255], 2, 2);
        for (var i = 0; i < png.Length; i++)
        {
            Assert.Throws<InvalidDataException>(() => Decode(png[..i], 2, 2));
            var bad = png.ToArray(); bad[i] ^= 1;
            Assert.Throws<InvalidDataException>(() => Decode(bad, 2, 2));
        }
        Assert.Throws<InvalidDataException>(() => Decode([..png, 0], 2, 2));
    }

    [Theory]
    [InlineData(0, 1)] [InlineData(1, 0)] [InlineData(-1, 1)]
    [InlineData(2049, 1)] [InlineData(1, 2049)] [InlineData(int.MaxValue, int.MaxValue)]
    public void InvalidDimensionsAreRejectedBeforeAllocation(int width, int height)
    {
        Assert.Throws<ArgumentException>(() => IndexedSourcePng.Encode(width, height, [], Palette));
        Assert.Throws<InvalidDataException>(() => IndexedSourcePng.Decode([], width, height, Palette));
    }

    [Fact]
    public void InvalidPaletteAndPixelIndicesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Encode([], 1, 1));
        Assert.Throws<InvalidDataException>(() => Encode([3], 1, 1));
        foreach (var palette in new[] { Array.Empty<string>(), new[] { "#112233" }, new[] { "112233", "#FFFFFF" },
            new[] { "#GG0000", "#FFFFFF" }, Enumerable.Repeat("#000000", 256).ToArray() })
            Assert.Throws<InvalidDataException>(() => IndexedSourcePng.Encode(1, 1, [0], palette));
        var png = Encode([0], 1, 1);
        Assert.Throws<InvalidDataException>(() => IndexedSourcePng.Decode(png, 1, 1, ["#112234", "#ABCDEF", "#112233"]));
        Assert.Throws<InvalidDataException>(() => Decode(Replace(png, "IDAT", Compress([0, 3])), 1, 1));
    }

    [Fact]
    public void RejectsValidChecksumsButInvalidHeaderPaletteTransparencyAndPixels()
    {
        var png = Encode([0], 1, 1);
        var chunks = Chunks(png);
        foreach (var index in new[] { 0, 4, 8, 9, 10, 11, 12 })
        {
            var header = chunks[0].Data.ToArray(); header[index] ^= 1;
            Assert.Throws<InvalidDataException>(() => Decode(Replace(png, "IHDR", header), 1, 1));
        }
        foreach (var index in new[] { 0, 12, 767 })
        {
            var palette = chunks.Single(c => c.Name == "PLTE").Data.ToArray(); palette[index] ^= 1;
            Assert.Throws<InvalidDataException>(() => Decode(Replace(png, "PLTE", palette), 1, 1));
        }
        foreach (var index in new[] { 0, 2, 254, 255 })
        {
            var alpha = chunks.Single(c => c.Name == "tRNS").Data.ToArray(); alpha[index] ^= 1;
            Assert.Throws<InvalidDataException>(() => Decode(Replace(png, "tRNS", alpha), 1, 1));
        }
        foreach (byte[] rows in new byte[][] { [], [0], [0, 0, 0], [5, 0] })
            Assert.Throws<InvalidDataException>(() => Decode(Replace(png, "IDAT", Compress(rows)), 1, 1));
        var compressed = Compress([0, 0]);
        Assert.Throws<InvalidDataException>(() => Decode(Replace(png, "IDAT", compressed[..^1]), 1, 1));
        compressed[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() => Decode(Replace(png, "IDAT", compressed), 1, 1));
    }

    [Fact]
    public void RejectsMissingDuplicateOutOfOrderAndUnknownCriticalChunks()
    {
        var chunks = Chunks(Encode([0], 1, 1));
        foreach (var name in new[] { "IHDR", "PLTE", "tRNS", "IDAT", "IEND" })
            Assert.Throws<InvalidDataException>(() => Decode(Build(chunks.Where(c => c.Name != name)), 1, 1));
        foreach (var name in new[] { "IHDR", "PLTE", "tRNS" })
        {
            var copy = chunks.ToList(); var index = copy.FindIndex(c => c.Name == name); copy.Insert(index, copy[index]);
            Assert.Throws<InvalidDataException>(() => Decode(Build(copy), 1, 1));
        }
        var swapped = chunks.ToList(); (swapped[1], swapped[2]) = (swapped[2], swapped[1]);
        Assert.Throws<InvalidDataException>(() => Decode(Build(swapped), 1, 1));
        var unknown = chunks.ToList(); unknown.Insert(1, new("ABCD", []));
        Assert.Throws<InvalidDataException>(() => Decode(Build(unknown), 1, 1));
        var nonemptyEnd = Replace(Build(chunks), "IEND", [0]);
        Assert.Throws<InvalidDataException>(() => Decode(nonemptyEnd, 1, 1));
    }

    [Fact]
    public void ConsecutiveSplitIdatChunksWorkButSeparatedChunksFail()
    {
        var chunks = Chunks(Encode([0, 1, 2, 255], 2, 2));
        var index = chunks.FindIndex(c => c.Name == "IDAT"); var data = chunks[index].Data;
        chunks[index] = new("IDAT", data[..3]); chunks.Insert(index + 1, new("IDAT", data[3..]));
        Assert.Equal(new byte[] { 0, 1, 2, 255 }, Decode(Build(chunks), 2, 2));
        chunks.Insert(index + 1, new("tEXt", "hello"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => Decode(Build(chunks), 2, 2));
    }

    private sealed record Chunk(string Name, byte[] Data);
    private static List<Chunk> Chunks(byte[] png)
    {
        var result = new List<Chunk>();
        for (var offset = 8; offset < png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            result.Add(new(Encoding.ASCII.GetString(png.AsSpan(offset + 4, 4)), png.AsSpan(offset + 8, length).ToArray()));
            offset += length + 12;
        }
        return result;
    }

    private static byte[] Replace(byte[] png, string name, byte[] replacement) =>
        Build(Chunks(png).Select(c => c.Name == name ? new Chunk(name, replacement) : c));

    // Independent fixture writer: deliberately simple bitwise CRC, separate from production codec.
    private static byte[] Build(IEnumerable<Chunk> chunks)
    {
        using var output = new MemoryStream(); output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        Span<byte> word = stackalloc byte[4];
        foreach (var chunk in chunks)
        {
            var type = Encoding.ASCII.GetBytes(chunk.Name);
            BinaryPrimitives.WriteInt32BigEndian(word, chunk.Data.Length); output.Write(word); output.Write(type); output.Write(chunk.Data);
            uint crc = uint.MaxValue;
            foreach (var value in type.Concat(chunk.Data))
            {
                crc ^= value;
                for (var i = 0; i < 8; i++) crc = crc >> 1 ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
            }
            BinaryPrimitives.WriteUInt32BigEndian(word, ~crc); output.Write(word);
        }
        return output.ToArray();
    }

    private static byte[] Compress(byte[] bytes)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZLibStream(stream, CompressionLevel.Optimal, true)) zip.Write(bytes);
        return stream.ToArray();
    }

    private static int Paeth(int left, int above, int corner)
    {
        var p = left + above - corner;
        var a = Math.Abs(p - left); var b = Math.Abs(p - above); var c = Math.Abs(p - corner);
        return a <= b && a <= c ? left : b <= c ? above : corner;
    }
}
