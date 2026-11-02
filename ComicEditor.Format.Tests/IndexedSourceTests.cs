using Google.Protobuf;
using Wire = ComicEditor.Wire;

namespace ComicEditor.Format.Tests;

public sealed class IndexedSourceTests
{
    [Fact]
    public void LegacyIndexedSourceRoundTripsEveryFieldWithExactPaletteIndices()
    {
        var scene = Cutscene.Create(257, 3);
        scene.Palette = Enumerable.Range(0, 255).Select(i => i < 2 ? "#123456" : $"#{i:X6}").ToList();
        var frame = scene.Frames[0]; frame.DurationMs = 4500; frame.Requirement = "not FLAG"; frame.TextVisible = false;
        frame.AttachAudio(QoaAudio.Encode(new short[800], 1, 8000));
        var layer = frame.Layers[0]; layer.Name = "Hidden artwork"; layer.Visible = false;
        for (var x = 0; x < 255; x++) layer.SetPixel(x, 0, x);
        frame.TextObjects.Add(new TextObject { Key = "hello", X = 1, Y = 2, Width = 100, Height = 20, Color = 1,
            RotationDegrees = 12, CurveDegrees = 20, FontSize = 16 });
        scene.Translations["en"]["hello"] = "Hello";
        var compressed = Wire.CutsceneDocument.Parser.ParseFrom(CutsceneFile.Write(scene));
        var legacy = compressed.Clone(); legacy.Version = 3;
        foreach (var f in legacy.Frames)
            foreach (var l in f.Layers)
            {
                l.Pixels = ByteString.CopyFrom(IndexedSourcePng.Decode(l.IndexedPng.Span, scene.Width, scene.Height, scene.Palette));
                l.IndexedPng = ByteString.Empty;
            }
        var restored = CutsceneFile.Parse(legacy.ToByteArray());
        Assert.Equal(compressed.ToByteArray(), CutsceneFile.Write(restored));
        var reread = CutsceneFile.Parse(compressed.ToByteArray());
        Assert.Equal(3, reread.Version); Assert.False(reread.IsRgba);
        Assert.Equal(scene.Palette, reread.Palette);
        Assert.Equal(0, reread.Frames[0].Layers[0].Pixel(0, 0));
        Assert.Equal(1, reread.Frames[0].Layers[0].Pixel(1, 0));
        Assert.Equal(254, reread.Frames[0].Layers[0].Pixel(254, 0));
        Assert.Equal(-1, reread.Frames[0].Layers[0].Pixel(255, 0));
        Assert.Equal(frame.AudioQoa, reread.Frames[0].AudioQoa);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("raw")]
    [InlineData("rgba")]
    [InlineData("qoi")]
    [InlineData("legacy")]
    public void RejectsMissingMixedOrWrongVersionPayloads(string kind)
    {
        var document = Wire.CutsceneDocument.Parser.ParseFrom(CutsceneFile.Write(Cutscene.Create(2, 1)));
        var layer = document.Frames[0].Layers[0];
        if (kind == "empty") layer.IndexedPng = ByteString.Empty;
        if (kind == "raw") layer.Pixels = ByteString.CopyFrom([0, 255]);
        if (kind == "rgba") layer.RgbaPng = ByteString.CopyFrom(RgbaPng.Encode(2, 1, [0u, 0u]));
        if (kind == "qoi") layer.RgbaQoi = ByteString.CopyFrom(RgbaQoi.Encode(2, 1, [0u, 0u]));
        if (kind == "legacy") document.Version = 3;
        Assert.Throws<InvalidDataException>(() => CutsceneFile.Parse(document.ToByteArray()));
    }

    [Fact]
    public void SparseSourceIsCompressedWithoutMutatingScene()
    {
        var scene = Cutscene.Create(1080, 720);
        scene.Frames[0].Layers[0].SetPixel(123, 456, 1);
        var bytes = CutsceneFile.Write(scene);
        Assert.True(bytes.Length < 10_000, $"Source was {bytes.Length} bytes");
        Assert.Equal(3, scene.Version);
        Assert.Equal(scene.Frames[0].Layers[0].Rows, CutsceneFile.Parse(bytes).Frames[0].Layers[0].Rows);
    }
}
