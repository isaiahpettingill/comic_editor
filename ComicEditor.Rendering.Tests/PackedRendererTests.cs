using ComicEditor.Format;
using ComicEditor.Rendering;

namespace ComicEditor.Rendering.Tests;

public sealed class PackedRendererTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PackedRowsMatchReferenceCompositeAcrossTileBoundaries(bool rgbaMode)
    {
        var scene = Cutscene.Create(67, 35, rgbaMode); var frame = scene.Frames[0];
        frame.Layers.Add(ArtworkLayer.Create("Upper", 67, 35, rgbaMode));
        frame.Layers.Add(ArtworkLayer.Create("Hidden", 67, 35, rgbaMode)); frame.Layers[2].Visible = false;
        var random = new Random(42);
        foreach (var layer in frame.Layers)
            for (var y = 0; y < scene.Height; y++)
                for (var x = 0; x < scene.Width; x++)
                {
                    if (random.Next(3) == 0) continue;
                    if (rgbaMode) layer.SetRgbaPixel(x, y, (uint)random.NextInt64(0, 0x100000000L));
                    else layer.SetPixel(x, y, random.Next(0, 16));
                }
        var colors = scene.Palette.Select(RgbaColor.Parse).ToArray();
        var output = new byte[scene.Width * 4]; var indices = new byte[scene.Width]; var rgba = new uint[scene.Width]; var composite = new uint[scene.Width];
        for (var y = 0; y < scene.Height; y++)
        {
            Array.Fill(output, (byte)123); Array.Fill(composite, 0x12345678u);
            CutsceneCanvas.CompositeArtworkRow(scene, frame, y, colors, output, indices, rgba, composite);
            for (var x = 0; x < scene.Width; x++)
            {
                uint expected = 0;
                foreach (var layer in frame.Layers.Where(l => l.Visible))
                    if (rgbaMode) expected = RgbaColor.Blend(layer.RgbaPixel(x, y), expected);
                    else if (layer.Pixel(x, y) >= 0) expected = colors[layer.Pixel(x, y)];
                if (rgbaMode) expected = RgbaColor.Blend(expected, 0xffffffff);
                Assert.Equal((byte)(expected >> 8), output[x * 4]);
                Assert.Equal((byte)(expected >> 16), output[x * 4 + 1]);
                Assert.Equal((byte)(expected >> 24), output[x * 4 + 2]);
                Assert.Equal((byte)(expected == 0 && !rgbaMode ? 0 : 255), output[x * 4 + 3]);
            }
        }
    }
}
