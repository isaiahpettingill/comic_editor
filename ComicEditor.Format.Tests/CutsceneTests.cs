using Google.Protobuf;

namespace ComicEditor.Format.Tests;

public class CutsceneTests
{
    [Fact]
    public void CanvasResizePreservesIndicesAndOffsetsAllFrames()
    {
        var scene = Cutscene.Create(4, 4);
        scene.Frames.Add(Frame.Create(4, 4));
        foreach (var frame in scene.Frames)
        {
            frame.Layers[0].SetPixel(1, 1, 127);
            frame.TextObjects.Add(new TextObject { X = 1, Y = 1 });
        }
        scene.ResizeCanvas(8, 6, true);
        foreach (var frame in scene.Frames)
        {
            Assert.Equal(127, frame.Layers[0].Pixel(3, 2)); Assert.Equal(-1, frame.Layers[0].Pixel(0, 0));
            Assert.Equal(3, frame.TextObjects[0].X); Assert.Equal(2, frame.TextObjects[0].Y);
        }
        scene.ResizeCanvas(4, 4, true);
        Assert.Equal(127, scene.Frames[0].Layers[0].Pixel(1, 1));
        scene.Validate();
        scene.ResizeCanvas(1, 1, false); Assert.Equal(-1, scene.Frames[0].Layers[0].Pixel(0, 0)); scene.Validate();
    }

    [Fact]
    public void ProtobufRoundTripPreservesArtLayersAndTranslations()
    {
        var scene = Cutscene.Create(12, 8);
        var layer = scene.Frames[0].Layers[0];
        Raster.Line(layer, 1, 1, 10, 6, 127); Raster.Fill(layer, 0, 0, 3);
        scene.Frames[0].TextObjects.Add(new TextObject { Key = "poseidon.ocean_warning", X = 2, Y = 3, Width = 9, Height = 4 });
        scene.Translations["en"]["poseidon.ocean_warning"] = "Hades is drinking the ocean!";
        scene.Translations["es"]["poseidon.ocean_warning"] = "¡Hades se está bebiendo el océano!";
        scene.Translations["pt"]["poseidon.ocean_warning"] = "Hades está bebendo o oceano!";
        scene.Translations["fr"] = new() { ["poseidon.ocean_warning"] = "L'océan !" };
        var result = CutsceneFile.Parse(CutsceneFile.Write(scene));
        Assert.Equal(128, result.Palette.Count); Assert.Equal(layer.Rows, result.Frames[0].Layers[0].Rows);
        Assert.Equal(127, result.Frames[0].Layers[0].Pixel(1, 1));
        Assert.Equal("¡Hades se está bebiendo el océano!", result.Text("es", "poseidon.ocean_warning"));
        Assert.Equal("L'océan !", result.Text("fr", "poseidon.ocean_warning"));
        Assert.Equal(scene.Frames[0].TextObjects[0].Key, result.Frames[0].TextObjects[0].Key);
    }

    [Fact]
    public void DynamicFallbackAndTextVisibilityRoundTrip()
    {
        var scene = Cutscene.Create(8, 8);
        scene.Translations.Clear(); scene.Translations["ja"] = new() { ["warning"] = "海" };
        scene.Translations["fr"] = new(); scene.FallbackLanguage = "ja"; scene.Frames[0].TextVisible = false;
        var restored = CutsceneFile.Parse(CutsceneFile.Write(scene));
        Assert.Equal("ja", restored.FallbackLanguage); Assert.False(restored.Frames[0].TextVisible);
        Assert.Equal("海", restored.RenderText("fr", "warning")); Assert.Equal("", restored.RenderText("fr", "missing"));
    }

    [Fact]
    public void ReaderRejectsOutOfRangePixel()
    {
        var document = new ComicEditor.Wire.CutsceneDocument { Version = 2, CanvasWidth = 1, CanvasHeight = 1 };
        document.PaletteRgb.Add(Enumerable.Repeat(0u, 128));
        var frame = new ComicEditor.Wire.Frame();
        frame.Layers.Add(new ComicEditor.Wire.ArtworkLayer { Pixels = ByteString.CopyFrom([128]) }); document.Frames.Add(frame);
        Assert.Throws<InvalidDataException>(() => CutsceneFile.Parse(document.ToByteArray()));
    }

    [Fact]
    public void ReaderUpgradesVersionOnePalette()
    {
        var document = new ComicEditor.Wire.CutsceneDocument { Version = 1, CanvasWidth = 2, CanvasHeight = 1 };
        document.PaletteRgb.Add(Enumerable.Repeat(0u, 16));
        var frame = new ComicEditor.Wire.Frame();
        frame.Layers.Add(new ComicEditor.Wire.ArtworkLayer { Pixels = ByteString.CopyFrom([15, 255]), Visible = true }); document.Frames.Add(frame);
        var scene = CutsceneFile.Parse(document.ToByteArray());
        Assert.Equal(2, scene.Version); Assert.Equal(128, scene.Palette.Count);
        Assert.Equal(15, scene.Frames[0].Layers[0].Pixel(0, 0)); Assert.Equal(-1, scene.Frames[0].Layers[0].Pixel(1, 0));
        Assert.True(scene.Frames[0].TextVisible);
    }
}
