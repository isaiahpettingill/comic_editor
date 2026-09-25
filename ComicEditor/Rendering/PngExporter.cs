using ComicEditor.Format;

namespace ComicEditor.Rendering;

public static class PngExporter
{
    public static void Write(Stream stream, Cutscene scene, int frameIndex, string language)
    {
        CutsceneFonts.RequireAvailable(scene, language);
        if (scene.IsRgba) { WriteRgba(stream, scene, frameIndex, language); return; }
        var frame = scene.Frames[frameIndex];
        var palette = scene.Palette.Select(c => Convert.ToUInt32(c[1..], 16)).ToArray();
        var rgb = Enumerable.Repeat(0xffffffu, scene.Width * scene.Height).ToArray();
        foreach (var layer in frame.Layers.Where(l => l.Visible))
            for (var y = 0; y < scene.Height; y++)
                for (var x = 0; x < scene.Width; x++)
                { var index = layer.Pixel(x, y); if (index >= 0) rgb[y * scene.Width + x] = palette[index]; }
        var colors = rgb.Distinct().ToList();
        var available = colors.ToHashSet();
        var approximations = new Dictionary<uint, uint>();
        if (frame.TextVisible)
            foreach (var obj in frame.TextObjects)
            {
                if (CutsceneFonts.IsCustom(obj.FontId) && !CutsceneFonts.IsInstalled(obj.FontId)) throw new InvalidDataException($"Required font is unavailable: {obj.FontId}.");
                foreach (var style in obj.Styles.Where(s => s.Language == language))
                    if (CutsceneFonts.IsCustom(style.FontId) && !CutsceneFonts.IsInstalled(style.FontId)) throw new InvalidDataException($"Required font is unavailable: {style.FontId}.");
                var text = scene.RenderText(language, obj.Key);
                if (string.IsNullOrWhiteSpace(text)) continue;
                var raster = DisplayCompiler.Rasterize(scene, obj, text, language); if (raster is null) continue;
                for (var y = 0; y < raster.Height; y++)
                    for (var x = 0; x < raster.Width; x++)
                    {
                        var alpha = raster.Alpha[(int)(y * raster.Width + x)];
                        if (alpha == 0) continue;
                        var offset = (raster.Y + y) * scene.Width + raster.X + x;
                        var foreground = palette[obj.Color];
                        // Sixteen coverage steps retain smooth edges without filling the
                        // limited PNG palette with nearly identical shades.
                        var coverage = (alpha + 8) / 17 * 17;
                        var blended = Blend(foreground, rgb[(int)offset], coverage);
                        if (!available.Contains(blended))
                        {
                            if (colors.Count < 256) { available.Add(blended); colors.Add(blended); }
                            else if (!approximations.TryGetValue(blended, out var nearest))
                            {
                                nearest = colors.MinBy(color => Distance(color, blended));
                                approximations.Add(blended, nearest);
                            }
                        }
                        rgb[(int)offset] = available.Contains(blended) ? blended : approximations[blended];
                    }
            }
        IndexedPng.Write(stream, scene.Width, scene.Height, rgb);
    }

    private static void WriteRgba(Stream stream, Cutscene scene, int frameIndex, string language)
    {
        var frame = scene.Frames[frameIndex]; var rgba = new uint[scene.Width * scene.Height];
        foreach (var layer in frame.Layers.Where(l => l.Visible))
            for (var y = 0; y < scene.Height; y++)
                for (var x = 0; x < scene.Width; x++)
                {
                    var offset = y * scene.Width + x;
                    rgba[offset] = RgbaColor.Blend(layer.RgbaPixel(x, y), rgba[offset]);
                }
        if (frame.TextVisible)
            foreach (var obj in frame.TextObjects)
            {
                if (CutsceneFonts.IsCustom(obj.FontId) && !CutsceneFonts.IsInstalled(obj.FontId)) throw new InvalidDataException($"Required font is unavailable: {obj.FontId}.");
                foreach (var style in obj.Styles.Where(s => s.Language == language))
                    if (CutsceneFonts.IsCustom(style.FontId) && !CutsceneFonts.IsInstalled(style.FontId)) throw new InvalidDataException($"Required font is unavailable: {style.FontId}.");
                var text = scene.RenderText(language, obj.Key);
                if (string.IsNullOrWhiteSpace(text)) continue;
                var raster = DisplayCompiler.Rasterize(scene, obj, text, language); if (raster is null) continue;
                var ink = RgbaColor.Parse(scene.Palette[obj.Color]);
                for (var y = 0; y < raster.Height; y++)
                    for (var x = 0; x < raster.Width; x++)
                    {
                        var alpha = raster.Alpha[(int)(y * raster.Width + x)];
                        if (alpha == 0) continue;
                        var offset = (int)((raster.Y + y) * scene.Width + raster.X + x);
                        rgba[offset] = RgbaColor.Blend(ink, rgba[offset], alpha);
                    }
            }
        stream.Write(RgbaPng.Encode(scene.Width, scene.Height, rgba));
    }

    private static uint Blend(uint foreground, uint background, int alpha)
    {
        if (alpha >= 255) return foreground;
        var inverse = 255 - alpha;
        var red = (((foreground >> 16) & 255) * (uint)alpha + ((background >> 16) & 255) * (uint)inverse + 127) / 255;
        var green = (((foreground >> 8) & 255) * (uint)alpha + ((background >> 8) & 255) * (uint)inverse + 127) / 255;
        var blue = ((foreground & 255) * (uint)alpha + (background & 255) * (uint)inverse + 127) / 255;
        return red << 16 | green << 8 | blue;
    }

    private static int Distance(uint first, uint second)
    {
        var red = (int)((first >> 16) & 255) - (int)((second >> 16) & 255);
        var green = (int)((first >> 8) & 255) - (int)((second >> 8) & 255);
        var blue = (int)(first & 255) - (int)(second & 255);
        return red * red + green * green + blue * blue;
    }
}
