using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ComicEditor.Display;
using ComicEditor.Format;
using Google.Protobuf;

namespace ComicEditor.Rendering;

public static class DisplayCompiler
{
    public static DisplayCutscene Compile(Cutscene scene)
    {
        scene.Validate();
        CutsceneFonts.RequireAvailable(scene);
        foreach (var obj in scene.Frames.Where(f => f.TextVisible).SelectMany(f => f.TextObjects))
            if (CutsceneFonts.IsCustom(obj.FontId) && !CutsceneFonts.IsInstalled(obj.FontId))
                throw new InvalidDataException($"Required font is unavailable: {obj.FontId}. Install it before compiling.");
        var result = new DisplayCutscene { Version = 2, CanvasWidth = (uint)scene.Width, CanvasHeight = (uint)scene.Height };
        result.PaletteRgb.Add(scene.Palette.Select(hex => Convert.ToUInt32(hex[1..], 16)));
        result.Languages.Add(scene.Translations.Keys.Order(StringComparer.Ordinal).ToArray());
        if (result.Languages.Count == 0) result.Languages.Add("und");
        result.FallbackLanguageIndex = (uint)Math.Max(0, result.Languages.IndexOf(scene.FallbackLanguage));
        foreach (var frame in scene.Frames)
        {
            var art = Enumerable.Repeat((byte)255, scene.Width * scene.Height).ToArray();
            foreach (var layer in frame.Layers.Where(l => l.Visible))
                for (var y = 0; y < scene.Height; y++)
                    for (var x = 0; x < scene.Width; x++)
                    { var index = layer.Pixel(x, y); if (index >= 0) art[y * scene.Width + x] = (byte)index; }
            var output = new DisplayFrame { IndexedArtwork = ByteString.CopyFrom(art) };
            foreach (var language in result.Languages)
            {
                var localized = new LocalizedFrameText();
                if (frame.TextVisible)
                    foreach (var obj in frame.TextObjects)
                    {
                        var text = scene.RenderText(language, obj.Key); if (string.IsNullOrWhiteSpace(text)) continue;
                        var raster = Rasterize(scene, obj, text, language);
                        if (raster is not null) localized.Runs.Add(raster);
                    }
                output.Text.Add(localized);
            }
            result.Frames.Add(output);
        }
        return result;
    }

    internal static TextRaster? Rasterize(Cutscene scene, TextObject obj, string text, string language)
    {
        var left = Math.Clamp((int)Math.Floor(obj.X), 0, scene.Width);
        var top = Math.Clamp((int)Math.Floor(obj.Y), 0, scene.Height);
        var right = Math.Clamp((int)Math.Ceiling(obj.X + obj.Width), 0, scene.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(obj.Y + obj.Height), 0, scene.Height);
        var width = right - left; var height = bottom - top; if (width <= 0 || height <= 0) return null;
        // The render target produces hard-edged glyph masks at native canvas size.
        // Supersample small text areas, with a memory cap for large canvases.
        var scale = 4;
        while (scale > 1 && (long)width * height * scale * scale * 4 > 32 * 1024 * 1024) scale /= 2;
        var pixelWidth = width * scale; var pixelHeight = height * scale;
        var view = new TextMask(obj, text, language, left, top, scale) { Width = pixelWidth, Height = pixelHeight };
        view.Measure(new Size(pixelWidth, pixelHeight)); view.Arrange(new Rect(0, 0, pixelWidth, pixelHeight));
        using var bitmap = new RenderTargetBitmap(new PixelSize(pixelWidth, pixelHeight), new Vector(96, 96)); bitmap.Render(view);
        var rgba = new byte[pixelWidth * pixelHeight * 4]; var handle = GCHandle.Alloc(rgba, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(0, 0, pixelWidth, pixelHeight), handle.AddrOfPinnedObject(), rgba.Length, pixelWidth * 4); }
        finally { handle.Free(); }
        var minX = width; var minY = height; var maxX = -1; var maxY = -1;
        var coverage = new byte[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var total = 0;
                for (var sy = 0; sy < scale; sy++)
                    for (var sx = 0; sx < scale; sx++)
                        total += rgba[((y * scale + sy) * pixelWidth + x * scale + sx) * 4 + 3];
                var sampleAlpha = (byte)((total + scale * scale / 2) / (scale * scale));
                coverage[y * width + x] = sampleAlpha;
                if (sampleAlpha == 0) continue;
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
        if (maxX < 0) return null;
        var croppedWidth = maxX - minX + 1; var croppedHeight = maxY - minY + 1;
        var alpha = new byte[croppedWidth * croppedHeight];
        for (var y = 0; y < croppedHeight; y++)
            for (var x = 0; x < croppedWidth; x++) alpha[y * croppedWidth + x] = coverage[(y + minY) * width + x + minX];
        return new TextRaster
        {
            X = (uint)(left + minX),
            Y = (uint)(top + minY),
            Width = (uint)croppedWidth,
            Height = (uint)croppedHeight,
            PaletteIndex = (uint)obj.Color,
            Alpha = ByteString.CopyFrom(alpha)
        };
    }

    private sealed class TextMask(TextObject obj, string text, string language, int left, int top, int scale) : Control
    {
        public override void Render(DrawingContext context)
        {
            var culture = CutsceneCanvas.Culture(language);
            var formatted = new FormattedText(text, culture, culture.TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                new Typeface(CutsceneFonts.Resolve(obj.FontId, language), obj.Italic ? FontStyle.Italic : FontStyle.Normal,
                    obj.Bold ? FontWeight.Bold : FontWeight.Normal), obj.FontSize, Brushes.White)
            { MaxTextWidth = obj.Width };
            using (context.PushTransform(Matrix.CreateScale(scale, scale)))
            using (context.PushClip(new Rect(obj.X - left, obj.Y - top, obj.Width, obj.Height)))
                context.DrawText(formatted, new Point(obj.X - left, obj.Y - top));
        }
    }
}
