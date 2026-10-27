using System.Buffers.Binary;
using ComicEditor.Format;

namespace ComicEditor.Format.Tests;

public sealed class RgbaQoiTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Qoi", name));
    private static uint[] Pixels(byte[] data) => Enumerable.Range(0, data.Length / 4).Select(i => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i * 4, 4))).ToArray();

    [Fact]
    public void OfficialReferenceMatchesBothDirectionsIncludingHiddenRgb()
    {
        var pixels = Pixels(Fixture("official-rgba-67x3.rgba"));
        var reference = Fixture("official-rgba-67x3.qoi");
        Assert.Equal(pixels, RgbaQoi.Decode(reference, 67, 3));
        var encoded = RgbaQoi.Encode(67, 3, pixels);
        Assert.Equal(reference, encoded);
        Assert.Equal(4, encoded[12]); Assert.Equal(0, encoded[13]);
        Assert.Equal(pixels, RgbaQoi.Decode(encoded, 67, 3));
    }

    [Fact]
    public void AcceptsRgbAndLinearMetadataWithoutChangingColors()
    {
        Assert.Equal(new uint[] { 0xff, 0xff0102ff, 0xff }, RgbaQoi.Decode(Fixture("official-rgb-3x1.qoi"), 3, 1));
        var data = RgbaQoi.Encode(1, 1, new uint[] { 0xabcdef00 }); data[13] = 1;
        Assert.Equal(new uint[] { 0xabcdef00 }, RgbaQoi.Decode(data, 1, 1));
    }

    [Fact]
    public void InitialBlackRunThenIndexAndTransparentIndexWork()
    {
        var header = RgbaQoi.Encode(2, 1, new uint[] { 0xff, 0xff })[..14];
        byte[] black = [..header, 0xc0, 53, 0, 0, 0, 0, 0, 0, 0, 1];
        Assert.Equal(new uint[] { 0xff, 0xff }, RgbaQoi.Decode(black, 2, 1));
        header = RgbaQoi.Encode(1, 1, new uint[] { 0 })[..14];
        byte[] transparent = [..header, 0, 0, 0, 0, 0, 0, 0, 0, 1];
        Assert.Equal(new uint[] { 0 }, RgbaQoi.Decode(transparent, 1, 1));
    }

    [Fact]
    public void DenseRandomPixelsAndLongRunsRoundTrip()
    {
        var random = new Random(913);
        foreach (var count in new[] { 1, 2, 62, 63, 64, 201, 2048 })
        {
            var pixels = Enumerable.Range(0, count).Select(_ => (uint)random.NextInt64(0, 0x100000000)).ToArray();
            Assert.Equal(pixels, RgbaQoi.Decode(RgbaQoi.Encode(count, 1, pixels), count, 1));
            Array.Fill(pixels, 0u);
            Assert.Equal(pixels, RgbaQoi.Decode(RgbaQoi.Encode(count, 1, pixels), count, 1));
        }
        var tall = new uint[2048];
        Assert.Equal(tall, RgbaQoi.Decode(RgbaQoi.Encode(1, 2048, tall), 1, 2048));
    }

    [Fact]
    public void EveryTruncationAndTrailingDataIsRejected()
    {
        var data = Fixture("official-rgba-67x3.qoi");
        for (var length = 0; length < data.Length; length++)
            Assert.Throws<InvalidDataException>(() => RgbaQoi.Decode(data.AsSpan(0, length), 67, 3));
        Assert.Throws<InvalidDataException>(() => RgbaQoi.Decode([..data, 0], 67, 3));
        Assert.Throws<InvalidDataException>(() => RgbaQoi.Decode([..data[..^8], 0xc0, ..data[^8..]], 67, 3));
    }

    [Theory]
    [InlineData(0, 1)] [InlineData(1, 0)] [InlineData(-1, 1)]
    [InlineData(2049, 1)] [InlineData(1, 2049)] [InlineData(int.MaxValue, int.MaxValue)]
    public void InvalidDimensionsAreRejectedBeforeAllocation(int width, int height)
    {
        Assert.Throws<InvalidDataException>(() => RgbaQoi.Encode(width, height, []));
        Assert.Throws<InvalidDataException>(() => RgbaQoi.Decode([], width, height));
    }

    [Fact]
    public void RejectsBadPixelCountHeaderAndMarker()
    {
        Assert.Throws<ArgumentException>(() => RgbaQoi.Encode(1, 1, []));
        var data = RgbaQoi.Encode(1, 1, new uint[] { 0x12345678 });
        Assert.Throws<InvalidDataException>(() => RgbaQoi.Decode(data, 2, 1));
        foreach (var offset in new[] { 4, 8 }) foreach (var value in new[] { 0u, 2049u, uint.MaxValue })
        {
            var bad = data.ToArray(); BinaryPrimitives.WriteUInt32BigEndian(bad.AsSpan(offset, 4), value);
            Assert.Throws<InvalidDataException>(() => RgbaQoi.Decode(bad, 1, 1));
        }
        foreach (var offset in new[] { 0, 12, 13, data.Length - 1 })
        {
            var bad = data.ToArray(); bad[offset] = 0xff;
            Assert.Throws<InvalidDataException>(() => RgbaQoi.Decode(bad, 1, 1));
        }
    }

    [Theory]
    [InlineData(0xff)] [InlineData(0xfe)] [InlineData(0x80)] [InlineData(0xc1)]
    public void TruncatedChunksAndRunOverrunAreRejected(byte tag)
    {
        var header = RgbaQoi.Encode(1, 1, new uint[] { 0x12345678 })[..14];
        byte[] malformed = [..header, tag, 0, 0, 0, 0, 0, 0, 0, 1];
        Assert.Throws<InvalidDataException>(() => RgbaQoi.Decode(malformed, 1, 1));
    }
}
