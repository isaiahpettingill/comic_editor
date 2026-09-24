using ComicEditor.Format;

namespace ComicEditor.Rendering;

public static class PngExporter
{
    public static void Write(Stream stream, Cutscene scene, int frameIndex, string language)
    {
        var frame = scene.Frames[frameIndex];
        var palette = scene.Palette.Select(c => Convert.ToUInt32(c[1..], 16)).ToArray();
        var rgb = Enumerable.Repeat(0xffffffu, scene.Width * scene.Height).ToArray();
        foreach (var layer in frame.Layers.Where(l => l.Visible))
            for (var y = 0; y < scene.Height; y++)
                for (var x = 0; x < scene.Width; x++)
                { var index = layer.Pixel(x, y); if (index >= 0) rgb[y * scene.Width + x] = palette[index]; }
        if (frame.TextVisible)
            foreach (var obj in frame.TextObjects)
            {
                if (CutsceneFonts.IsCustom(obj.FontId) && !CutsceneFonts.IsInstalled(obj.FontId)) throw new InvalidDataException($"Required font is unavailable: {obj.FontId}.");
                var text = scene.RenderText(language, obj.Key);
                if (string.IsNullOrWhiteSpace(text)) continue;
                var raster = DisplayCompiler.Rasterize(scene, obj, text, language); if (raster is null) continue;
                for (var y = 0; y < raster.Height; y++)
                    for (var x = 0; x < raster.Width; x++)
                        if (raster.Alpha[(int)(y * raster.Width + x)] >= 128)
                            rgb[(raster.Y + y) * scene.Width + raster.X + x] = palette[obj.Color];
            }
        IndexedPng.Write(stream, scene.Width, scene.Height, rgb);
    }
}
