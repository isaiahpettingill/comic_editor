using System.Buffers.Binary;
using Avalonia.Headless;
using ComicEditor.Format;
using ComicEditor.Rendering;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Theory]
    [InlineData("comic-shanns", false, false)]
    [InlineData("google:Permanent Marker", true, true)]
    public async Task IndexedPngKeepsAntialiasedFontEdges(string fontId, bool bold, bool italic)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var scene = Cutscene.Create(320, 180);
            scene.Palette = ["#17171D", "#FFFFFF"];
            scene.Frames[0].TextObjects.Add(new TextObject
            {
                Key = "line",
                X = 16,
                Y = 16,
                Width = 250,
                Height = 90,
                FontId = fontId,
                FontSize = 20,
                Bold = bold,
                Italic = italic,
                Color = 0
            });
            scene.Translations["en"]["line"] = "I love you\nEllie!";
            using var output = new MemoryStream();
            PngExporter.Write(output, scene, 0, "en");
            var png = output.ToArray();
            var raster = DisplayCompiler.Rasterize(scene, scene.Frames[0].TextObjects[0], scene.Translations["en"]["line"], "en");
            Assert.NotNull(raster);
            Assert.Contains(raster.Alpha, alpha => alpha is > 0 and < 255);
            Assert.Equal(3, png[25]); // Indexed PNG color type.
            var paletteSize = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(33, 4));
            Assert.InRange(paletteSize / 3, 3, 256);
            var shades = png.AsSpan(41, paletteSize).ToArray();
            Assert.Contains(Enumerable.Range(0, paletteSize / 3), i =>
                shades[i * 3] is > 23 and < 255 && shades[i * 3 + 1] is > 23 and < 255 && shades[i * 3 + 2] is > 29 and < 255);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TextExportStillFitsWhenArtworkUsesTheFullIndexedPalette()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var scene = Cutscene.Create(320, 180);
            scene.Palette = Enumerable.Range(0, 255).Select(i => $"#{i:X2}00{255 - i:X2}").ToList();
            for (var x = 0; x < 255; x++) scene.Frames[0].Layers[0].SetPixel(x, 100, x);
            scene.Frames[0].TextObjects.Add(new TextObject
            {
                Key = "line",
                X = 16,
                Y = 16,
                Width = 250,
                Height = 60,
                FontId = "comic-shanns",
                FontSize = 20,
                Color = 0
            });
            scene.Translations["en"]["line"] = "Hello";
            using var output = new MemoryStream();
            PngExporter.Write(output, scene, 0, "en");
            var png = output.ToArray();
            Assert.Equal(3, png[25]);
            Assert.InRange(BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(33, 4)) / 3, 2, 256);
        }, CancellationToken.None);
    }
}
