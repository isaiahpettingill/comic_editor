using ComicEditor.Wire;

namespace ComicEditor.Format.Tests;

public sealed class RgbaTests
{
    [Fact]
    public void RgbaPngAndProjectRoundTripBeyondIndexedPaletteLimit()
    {
        var scene = Cutscene.Create(3, 2, rgba: true);
        scene.Palette = Enumerable.Range(0, 300).Select(i => $"#{i:X6}80").ToList();
        var layer = scene.Frames[0].Layers[0];
        PaintRaster.Stamp(layer, 0, 0, 299, 1, BrushTip.Square, scene.Palette);
        PaintRaster.Stamp(layer, 0, 0, 299, 1, BrushTip.Square, scene.Palette);
        Assert.Equal(192u, layer.RgbaPixel(0, 0) & 255);
        var bytes = CutsceneFile.Write(scene);
        var wire = CutsceneDocument.Parser.ParseFrom(bytes);
        Assert.Equal(4u, wire.Version);
        Assert.Equal(300, wire.PaletteRgba.Count);
        Assert.Equal(6, wire.Frames[0].Layers[0].RgbaPng.Span[25]);
        var restored = CutsceneFile.Parse(bytes);
        Assert.Equal(scene.Palette, restored.Palette);
        Assert.Equal(layer.Rows, restored.Frames[0].Layers[0].Rows);
        restored.ResizeCanvas(5, 4, center: true);
        Assert.Equal(layer.RgbaPixel(0, 0), restored.Frames[0].Layers[0].RgbaPixel(1, 1));
    }

    [Fact]
    public void IndexedConversionPreservesPixelsAndLeavesTransparency()
    {
        var scene = Cutscene.Create(2, 1);
        scene.Frames[0].Layers[0].SetPixel(0, 0, 2);
        var expected = RgbaColor.Parse(scene.Palette[2]);
        scene.ConvertToRgba();
        Assert.Equal(4, scene.Version);
        Assert.Equal(expected, scene.Frames[0].Layers[0].RgbaPixel(0, 0));
        Assert.Equal(0u, scene.Frames[0].Layers[0].RgbaPixel(1, 0));
        Assert.Equal("#17171DFF", scene.Palette[0]);
    }

    [Fact]
    public void DitherAndScrambleWorkInBothModes()
    {
        foreach (var rgba in new[] { false, true })
        {
            var scene = Cutscene.Create(16, 16, rgba);
            var layer = scene.Frames[0].Layers[0];
            PaintRaster.Dither(layer, 8, 8, 8, 8, 0, 8, 8, scene.Palette);
            var before = layer.Rows.ToArray();
            Assert.Contains(before, row => rgba ? row.Contains("17171DFF") : row.Contains("00"));
            PaintRaster.Scramble(layer, 7, 7, 9, 9, 8, new Random(7));
            Assert.False(before.SequenceEqual(layer.Rows));
            var count = (string row) => Enumerable.Range(0, 16).Count(x => rgba ? Convert.ToUInt32(row.Substring(x * 8, 8), 16) != 0 : row.Substring(x * 2, 2) != "FF");
            Assert.Equal(before.Sum(count), layer.Rows.Sum(count));
        }
    }

    [Fact]
    public void RgbaPngRejectsCorruption()
    {
        var bytes = RgbaPng.Encode(1, 1, [0x12345678]);
        Assert.Equal(0x12345678u, Assert.Single(RgbaPng.Decode(bytes, 1, 1)));
        bytes[45] ^= 1;
        Assert.Throws<InvalidDataException>(() => RgbaPng.Decode(bytes, 1, 1));
    }

    [Fact]
    public void PerLanguageTextPlacementRoundTripsAndResizes()
    {
        var scene = Cutscene.Create(100, 50);
        var text = new TextObject { Key = "line", X = 1, Y = 2, Width = 30, Height = 10 };
        text.SetPlacement("es", "en", new TextPlacement { X = 12, Y = 14, Width = 40, Height = 12 });
        scene.Frames[0].TextObjects.Add(text);
        var restored = CutsceneFile.Parse(CutsceneFile.Write(scene));
        var obj = restored.Frames[0].TextObjects[0];
        Assert.Equal(1, obj.Placement("en", "en").X);
        Assert.Equal(12, obj.Placement("es", "en").X);
        Assert.Equal(1, obj.Placement("pt", "en").X);
        restored.ResizeCanvas(120, 70, center: true);
        Assert.Equal(11, obj.Placement("en", "en").X);
        Assert.Equal(22, obj.Placement("es", "en").X);
    }

    [Fact]
    public void HighlightedTextStylesTrackEditsAndRoundTrip()
    {
        var scene = Cutscene.Create(100, 50);
        var obj = new TextObject { Key = "line" };
        obj.ChangeStyle("es", "Hola", 1, 2, sizeDelta: 4, fontId: "google:Anton");
        obj.RetargetStyles("es", "Hola", "HoXla");
        scene.Frames[0].TextObjects.Add(obj);
        var restored = CutsceneFile.Parse(CutsceneFile.Write(scene)).Frames[0].TextObjects[0];
        Assert.Equal(20, restored.StyleAt("es", "HoXla", 2).Size);
        Assert.Equal("google:Anton", restored.StyleAt("es", "HoXla", 2).FontId);
        Assert.Equal(16, restored.StyleAt("en", "Hello", 2).Size);
    }

    [Fact]
    public void BlurSoftensArtworkInIndexedAndRgbaModes()
    {
        foreach (var rgba in new[] { false, true })
        {
            var scene = Cutscene.Create(5, 1, rgba);
            scene.Palette = rgba ? ["#000000FF", "#FFFFFFFF", "#AAAAAAFF"] : ["#000000", "#FFFFFF", "#AAAAAA"];
            var layer = scene.Frames[0].Layers[0];
            for (var x = 0; x < 5; x++)
                if (rgba) layer.SetRgbaPixel(x, 0, x == 2 ? 0x000000FF : 0xFFFFFFFF);
                else layer.SetPixel(x, 0, x == 2 ? 0 : 1);
            PaintRaster.Blur(layer, 2, 0, 2, 0, 2, scene.Palette);
            if (rgba) Assert.Equal(0xAAAAAAFFu, layer.RgbaPixel(2, 0));
            else Assert.Equal(2, layer.Pixel(2, 0));
        }
    }
}
