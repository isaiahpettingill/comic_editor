using ComicEditor.Format;

namespace ComicEditor.Format.Tests;

public sealed class ArtworkBufferTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyCanvasUsesNoTilesAndErasingLastPixelReleasesTile(bool rgba)
    {
        var layer = ArtworkLayer.Create("art", 2048, 2048, rgba);
        Assert.Equal(0, layer.AllocatedTileCount);
        if (rgba) layer.SetRgbaPixel(32, 33, 0x12345678); else layer.SetPixel(32, 33, 12);
        Assert.Equal(1, layer.AllocatedTileCount);
        if (rgba) layer.SetRgbaPixel(32, 33, 0); else layer.SetPixel(32, 33, -1);
        Assert.Equal(0, layer.AllocatedTileCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SnapshotAndRestoreKeepBothOwnersIsolated(bool rgba)
    {
        var layer = ArtworkLayer.Create("art", 65, 33, rgba);
        void Set(ArtworkLayer target, int x, int y, int color)
        { if (rgba) target.SetRgbaPixel(x, y, (uint)color); else target.SetPixel(x, y, color); }
        Set(layer, 0, 0, 1); Set(layer, 32, 32, 2); Set(layer, 64, 32, 3);
        var copy = layer.Snapshot(); Assert.True(layer.PixelsEqual(copy));
        Set(layer, 0, 0, 4); Set(copy, 32, 32, 5);
        Assert.False(layer.PixelsEqual(copy));
        Assert.Equal(rgba ? 1u : 1, rgba ? copy.RgbaPixel(0, 0) : (uint)copy.Pixel(0, 0));
        Assert.Equal(rgba ? 2u : 2, rgba ? layer.RgbaPixel(32, 32) : (uint)layer.Pixel(32, 32));
        layer.RestorePixels(copy); Assert.True(layer.PixelsEqual(copy));
        Set(layer, 64, 32, 6); Assert.False(layer.PixelsEqual(copy));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DenseRandomPixelsAndRowsMatchAcrossTileEdges(bool rgba)
    {
        const int width = 67, height = 39;
        var layer = ArtworkLayer.Create("art", width, height, rgba); var random = new Random(73);
        var expected = new uint[width * height];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var value = rgba ? (uint)random.NextInt64(0, 0x1_0000_0000) : (uint)random.Next(0, 255);
            expected[y * width + x] = value;
            if (rgba) layer.SetRgbaPixel(x, y, value); else layer.SetPixel(x, y, (int)value);
        }
        Assert.Equal(6, layer.AllocatedTileCount);
        for (var y = 0; y < height; y++)
        {
            if (rgba) { var row = new uint[width]; layer.ReadRgbaRow(y, row); Assert.Equal(expected.AsSpan(y * width, width).ToArray(), row); }
            else { var row = new byte[width]; layer.ReadIndexedRow(y, row); Assert.Equal(expected.AsSpan(y * width, width).ToArray().Select(x => (byte)x), row); }
        }
        var fromRows = new ArtworkLayer { IsRgba = rgba, Rows = layer.Rows.ToList() };
        Assert.True(layer.PixelsEqual(fromRows));
    }

    [Fact]
    public void LegacyRowReplacementInvalidatesBufferWithoutMutatingSnapshot()
    {
        var layer = ArtworkLayer.Create("art", 2, 2); layer.SetPixel(0, 0, 1);
        var original = layer.Snapshot();
        layer.Rows[0] = "0203";
        Assert.Equal(2, layer.Pixel(0, 0)); Assert.Equal(3, layer.Pixel(1, 0));
        Assert.Equal(1, original.Pixel(0, 0)); Assert.Equal(-1, original.Pixel(1, 0));
    }

    [Fact]
    public void BufferBackedFormatRoundTripPreservesBytesAndSparseTransparency()
    {
        foreach (var rgba in new[] { false, true })
        {
            var scene = Cutscene.Create(67, 39, rgba);
            var layer = scene.Frames[0].Layers[0];
            if (rgba) layer.SetRgbaPixel(66, 38, 0x22334455); else layer.SetPixel(66, 38, 2);
            var bytes = CutsceneFile.Write(scene); var restored = CutsceneFile.Parse(bytes);
            Assert.Equal(bytes, CutsceneFile.Write(restored));
            Assert.True(layer.PixelsEqual(restored.Frames[0].Layers[0]));
            Assert.Equal(1, restored.Frames[0].Layers[0].AllocatedTileCount);
        }
    }
}
