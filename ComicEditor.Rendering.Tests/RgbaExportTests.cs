using ComicEditor.Format;
using ComicEditor.Rendering;

namespace ComicEditor.Rendering.Tests;

public sealed class RgbaExportTests
{
    [Fact]
    public void ExportAndGameAssetKeepCompositedAlphaAndIndexedStillExportsIndexed()
    {
        var scene = Cutscene.Create(2, 1, rgba: true);
        scene.Palette = ["#FF000080", "#0000FF80"];
        var baseLayer = scene.Frames[0].Layers[0];
        baseLayer.SetRgbaPixel(0, 0, RgbaColor.Parse(scene.Palette[0]));
        var top = ArtworkLayer.Create("top", 2, 1, rgba: true);
        top.SetRgbaPixel(0, 0, RgbaColor.Parse(scene.Palette[1]));
        scene.Frames[0].Layers.Add(top);
        var expected = RgbaColor.Blend(top.RgbaPixel(0, 0), baseLayer.RgbaPixel(0, 0));
        using var output = new MemoryStream(); PngExporter.Write(output, scene, 0, "en");
        var png = output.ToArray();
        Assert.Equal(6, png[25]);
        Assert.Equal(expected, RgbaPng.Decode(png, 2, 1)[0]);
        var compiled = DisplayCompiler.Compile(scene);
        Assert.Equal(3u, compiled.Version);
        Assert.Equal(expected, RgbaPng.Decode(compiled.Frames[0].RgbaArtworkPng.Span, 2, 1)[0]);
        Assert.Equal(255u, compiled.PaletteRgba[0] >> 24);
        var indexed = Cutscene.Create(2, 1);
        using var oldPng = new MemoryStream(); PngExporter.Write(oldPng, indexed, 0, "en");
        Assert.Equal(3, oldPng.ToArray()[25]);
        Assert.Equal(2u, DisplayCompiler.Compile(indexed).Version);
        using var pdf = new MemoryStream(); BookExporter.Write(pdf, scene, "en", BookFormat.Pdf);
        Assert.Contains("/DeviceRGB", System.Text.Encoding.ASCII.GetString(pdf.ToArray()));
    }
}
