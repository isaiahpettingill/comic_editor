using Google.Protobuf;
using Wire = ComicEditor.Wire;

namespace ComicEditor.Format.Tests;

public sealed class SourceQoiTests
{
    private static Wire.CutsceneDocument LegacyPngDocument()
    {
        // Independent old-format document, not produced by the new source writer.
        var document = new Wire.CutsceneDocument { Version = 4, CanvasWidth = 2, CanvasHeight = 1, FallbackLanguage = "en" };
        document.PaletteRgba.Add([0x000000FFu, 0xFFFFFFFFu]);
        var frame = new Wire.Frame { Id = "frame", DurationMs = 4500, Req = "always" };
        frame.Layers.Add(new Wire.ArtworkLayer { Id = "layer", Name = "Legacy", Visible = true,
            RgbaPng = ByteString.CopyFrom(RgbaPng.Encode(2, 1, [0x12345678u, 0xABCDEF00u])) });
        document.Frames.Add(frame);
        document.Languages.Add(new Wire.Language { Code = "en" });
        return document;
    }

    [Fact]
    public void LegacyPngSourceLoadsAndResavesAsQoiWithoutChangingModelOrHiddenRgb()
    {
        var scene = CutsceneFile.Parse(LegacyPngDocument().ToByteArray());
        Assert.Equal(4, scene.Version); Assert.True(scene.IsRgba);
        Assert.Equal(4500, scene.Frames[0].DurationMs);
        Assert.Equal(0x12345678u, scene.Frames[0].Layers[0].RgbaPixel(0, 0));
        Assert.Equal(0xABCDEF00u, scene.Frames[0].Layers[0].RgbaPixel(1, 0));
        var bytes = CutsceneFile.Write(scene);
        var wire = Wire.CutsceneDocument.Parser.ParseFrom(bytes);
        Assert.Equal(5u, wire.Version); Assert.Equal(4, scene.Version);
        var layer = Assert.Single(Assert.Single(wire.Frames).Layers);
        Assert.True(layer.Pixels.IsEmpty); Assert.True(layer.RgbaPng.IsEmpty);
        Assert.True(layer.RgbaQoi.Span[..4].SequenceEqual("qoif"u8));
        Assert.Equal(scene.Frames[0].Layers[0].Rows, CutsceneFile.Parse(bytes).Frames[0].Layers[0].Rows);
    }

    [Fact]
    public void IndexedSourceRemainsVersionThreeAndUnencoded()
    {
        var scene = Cutscene.Create(2, 1); scene.Frames[0].Layers[0].SetPixel(0, 0, 1);
        var wire = Wire.CutsceneDocument.Parser.ParseFrom(CutsceneFile.Write(scene));
        Assert.Equal(3u, wire.Version);
        var layer = wire.Frames[0].Layers[0];
        Assert.Equal(new byte[] { 1, 255 }, layer.Pixels.ToByteArray());
        Assert.True(layer.RgbaPng.IsEmpty); Assert.True(layer.RgbaQoi.IsEmpty);
    }

    [Theory]
    [InlineData(4u, "qoi")]
    [InlineData(4u, "both")]
    [InlineData(4u, "indexed")]
    [InlineData(4u, "empty")]
    [InlineData(5u, "png")]
    [InlineData(5u, "both")]
    [InlineData(5u, "indexed")]
    [InlineData(5u, "empty")]
    public void RgbaSourceRejectsWrongMissingOrAmbiguousPayload(uint version, string kind)
    {
        var document = LegacyPngDocument(); document.Version = version;
        var layer = document.Frames[0].Layers[0];
        if (kind is "qoi" or "both" || (kind == "indexed" && version == 5))
            layer.RgbaQoi = ByteString.CopyFrom(RgbaQoi.Encode(2, 1, [0x12345678u, 0u]));
        if (kind is "qoi" or "empty" || (kind == "indexed" && version == 5)) layer.RgbaPng = ByteString.Empty;
        if (kind == "indexed") layer.Pixels = ByteString.CopyFrom([0, 1]);
        Assert.Throws<InvalidDataException>(() => CutsceneFile.Parse(document.ToByteArray()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndexedSourceRejectsImagePayloads(bool qoi)
    {
        var document = Wire.CutsceneDocument.Parser.ParseFrom(CutsceneFile.Write(Cutscene.Create(2, 1)));
        var layer = document.Frames[0].Layers[0];
        if (qoi) layer.RgbaQoi = ByteString.CopyFrom(RgbaQoi.Encode(2, 1, [0u, 0u]));
        else layer.RgbaPng = ByteString.CopyFrom(RgbaPng.Encode(2, 1, [0u, 0u]));
        Assert.Throws<InvalidDataException>(() => CutsceneFile.Parse(document.ToByteArray()));
    }

    [Fact]
    public void QoiSourceRejectsDimensionMismatchAndUnknownVersion()
    {
        var document = LegacyPngDocument(); document.Version = 5;
        var layer = document.Frames[0].Layers[0]; layer.RgbaPng = ByteString.Empty;
        layer.RgbaQoi = ByteString.CopyFrom(RgbaQoi.Encode(1, 1, [0u]));
        Assert.Throws<InvalidDataException>(() => CutsceneFile.Parse(document.ToByteArray()));
        document.Version = 6;
        var error = Assert.Throws<InvalidDataException>(() => CutsceneFile.Parse(document.ToByteArray()));
        Assert.Contains("Unsupported cutscene version", error.Message);
    }
}
