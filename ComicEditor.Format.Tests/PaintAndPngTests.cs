using System.Buffers.Binary;
using System.IO.Compression;
using ComicEditor.Format;

namespace ComicEditor.Format.Tests;

public class PaintAndPngTests
{
    [Fact]
    public void SnapshotKeepsArtworkAndTextStableWhileOriginalChanges()
    {
        var scene = Cutscene.Create(4, 4, rgba: true);
        scene.Frames[0].TextObjects.Add(new TextObject { Key = "line", Styles = [new TextStyleSpan { FontSize = 18, Length = 1 }] });
        scene.Translations["en"]["line"] = "before";
        var snapshot = scene.Snapshot();
        var expected = CutsceneFile.Write(snapshot);

        scene.Frames[0].Layers[0].SetRgbaPixel(0, 0, 0xff0000ff);
        scene.Frames[0].TextObjects[0].Styles[0].FontSize = 32;
        scene.Translations["en"]["line"] = "after";
        scene.Palette[0] = "#FFFFFFFF";

        Assert.Equal(expected, CutsceneFile.Write(snapshot));
        Assert.NotEqual(expected, CutsceneFile.Write(scene));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 4)]
    [InlineData(16, 4)]
    [InlineData(17, 8)]
    [InlineData(128, 8)]
    [InlineData(256, 8)]
    public void PngUsesExactPaletteAndPacksOddWidthRows(int colors, int depth)
    {
        const int width = 259, height = 3;
        var pixels = Enumerable.Range(0, width * height).Select(i => (uint)(i % colors) * 0x010101).ToArray();
        using var png = new MemoryStream(); IndexedPng.Write(png, width, height, pixels);
        var bytes = png.ToArray(); Assert.Equal(depth, bytes[24]); Assert.Equal(3, bytes[25]);
        Assert.Equal(colors * 3, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(33)));
        var dataOffset = 33 + 12 + colors * 3;
        var compressedLength = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(dataOffset));
        using var compressed = new MemoryStream(bytes, dataOffset + 8, compressedLength);
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var rows = new MemoryStream(); zlib.CopyTo(rows); var scanlines = rows.ToArray();
        var stride = (width * depth + 7) / 8 + 1;
        Assert.Equal(stride * height, scanlines.Length);
        for (var y = 0; y < height; y++)
        {
            Assert.Equal(0, scanlines[y * stride]);
            for (var x = 0; x < width; x++)
            {
                var index = (scanlines[y * stride + 1 + x * depth / 8] >> (8 - depth - x * depth % 8)) & ((1 << depth) - 1);
                Assert.Equal(pixels[y * width + x], (uint)bytes[41 + index * 3] * 0x010101);
            }
        }
    }

    [Fact]
    public void ShapedBrushesClipAndKeepTheirDistinctFootprints()
    {
        var masks = new HashSet<string>();
        foreach (var tip in Enum.GetValues<BrushTip>())
        {
            var layer = ArtworkLayer.Create("test", 12, 12);
            PaintRaster.Stamp(layer, 5, 5, 127, 6, tip);
            masks.Add(string.Join("", layer.Rows));
            PaintRaster.Stroke(layer, 0, 0, 11, 11, 2, 12, tip);
            Assert.Equal(12, layer.Rows.Count); Assert.All(layer.Rows, r => Assert.Equal(24, r.Length));
        }
        Assert.Equal(6, masks.Count);
    }

    [Fact]
    public void SprayAndShapesUseIndexedPixelsAndRespectCanvasEdges()
    {
        var layer = ArtworkLayer.Create("test", 40, 40);
        PaintRaster.Spray(layer, 0, 0, 127, 16, 100, new Random(123));
        Assert.Contains(layer.Rows, row => row.Contains("7F")); Assert.Equal(-1, layer.Pixel(12, 12));
        PaintRaster.Polygon(layer, PaintRaster.Shape("RoundedRectangle", 5, 5, 35, 35), 2, 1, ShapeFill.Solid);
        Assert.Equal(2, layer.Pixel(20, 20)); Assert.Equal(-1, layer.Pixel(35, 35));
        PaintRaster.Curve(layer, (0, 39), (39, 39), (10, 2), (30, 2), 4, 1);
        Assert.Equal(4, layer.Pixel(0, 39)); Assert.Equal(4, layer.Pixel(39, 39)); Assert.Contains(layer.Rows.Take(25), row => row.Contains("04"));
    }
}
