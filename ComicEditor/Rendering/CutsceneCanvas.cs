using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;
using ComicEditor.Format;

namespace ComicEditor.Rendering;

// Games can use this control with the shared Format project to render .cutscene files.
public sealed class CutsceneCanvas : Control
{
    public CutsceneCanvas() => RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
    public Cutscene? Scene { get; set; }
    public int FrameIndex { get; set; }
    public string Language { get; set; } = "en";
    public bool OnionSkin { get; set; }
    public double OnionOpacity { get; set; } = 0.35;
    public bool ShowTextBounds { get; set; }
    public string? SelectedTextId { get; set; }
    public string? HiddenTextId { get; set; }
    public bool TextVisible { get; set; } = true;
    public Rect? DraftTextBounds { get; set; }
    public Rect? SelectionBounds { get; set; }
    public Point[]? SelectionOutline { get; set; }

    public static readonly FontFamily DefaultFont = CutsceneFonts.Resolve("comic-shanns");

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Scene is null || FrameIndex < 0 || FrameIndex >= Scene.Frames.Count) return;
        var scale = Math.Min(Bounds.Width / Scene.Width, Bounds.Height / Scene.Height);
        if (scale <= 0) return;
        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, Scene.Width, Scene.Height));
            if (OnionSkin && FrameIndex > 0)
                using (context.PushOpacity(Math.Clamp(OnionOpacity, 0, 1)))
                    RenderFrame(context, Scene, FrameIndex - 1, Language, false, false, null, false);
            RenderFrame(context, Scene, FrameIndex, Language, TextVisible, ShowTextBounds, SelectedTextId, OnionSkin, HiddenTextId);
            if (DraftTextBounds is Rect draft)
                context.DrawRectangle(null, new Pen(Brushes.DodgerBlue, 1), draft);
            var selectionPen = new Pen(Brushes.DodgerBlue, 1, DashStyle.Dash);
            if (SelectionBounds is Rect selection) context.DrawRectangle(null, selectionPen, selection);
            if (SelectionOutline is { Length: > 1 } points)
                for (var i = 0; i < points.Length; i++) context.DrawLine(selectionPen, points[i], points[(i + 1) % points.Length]);
        }
    }

    public Point CanvasPoint(PointerEventArgs e)
    {
        if (Scene is null) return default;
        var scale = Math.Min(Bounds.Width / Scene.Width, Bounds.Height / Scene.Height);
        var p = e.GetPosition(this);
        return new Point(Math.Clamp((int)(p.X / scale), 0, Scene.Width - 1), Math.Clamp((int)(p.Y / scale), 0, Scene.Height - 1));
    }

    public static void RenderFrame(DrawingContext context, Cutscene scene, int frameIndex, string language,
        bool textVisible = true, bool showBounds = false, string? selectedId = null, bool onionBackground = false, string? hiddenTextId = null)
    {
        var frame = scene.Frames[frameIndex];
        var brushes = scene.Palette.Select(hex => { var c = RgbaColor.Parse(hex); return new SolidColorBrush(Color.FromArgb((byte)c, (byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8))); }).ToArray();
        // Composite indices before scaling. Adjacent antialiased rectangles leave seams
        // at fractional zoom; a single nearest-neighbor bitmap preserves solid pixels.
        var pixels = new byte[scene.Width * scene.Height * 4];
        var colors = scene.Palette.Select(RgbaColor.Parse).ToArray();
        var composite = scene.IsRgba ? new uint[scene.Width * scene.Height] : null;
        foreach (var layer in frame.Layers.Where(l => l.Visible))
        {
            for (var y = 0; y < scene.Height; y++)
            {
                var row = Convert.FromHexString(layer.Rows[y]);
                for (var x = 0; x < scene.Width; x++)
                {
                    var color = scene.IsRgba ? 0 : row[x];
                    if (!scene.IsRgba && color == 255) continue;
                    var offset = (y * scene.Width + x) * 4;
                    var rgba = scene.IsRgba ?
                        ((uint)row[x * 4] << 24) | ((uint)row[x * 4 + 1] << 16) | ((uint)row[x * 4 + 2] << 8) | row[x * 4 + 3]
                        : colors[color];
                    if (scene.IsRgba)
                    {
                        var pixelIndex = y * scene.Width + x;
                        composite![pixelIndex] = RgbaColor.Blend(rgba, composite[pixelIndex]);
                    }
                    else
                    {
                        pixels[offset] = (byte)(rgba >> 8); pixels[offset + 1] = (byte)(rgba >> 16);
                        pixels[offset + 2] = (byte)(rgba >> 24); pixels[offset + 3] = 255;
                    }
                }
            }
        }
        if (scene.IsRgba)
            for (var i = 0; i < composite!.Length; i++)
            {
                var rgba = RgbaColor.Blend(composite[i], 0xffffffff);
                pixels[i * 4] = (byte)(rgba >> 8); pixels[i * 4 + 1] = (byte)(rgba >> 16);
                pixels[i * 4 + 2] = (byte)(rgba >> 24); pixels[i * 4 + 3] = 255;
            }
        using (var bitmap = new WriteableBitmap(new PixelSize(scene.Width, scene.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul))
        {
            using (var buffer = bitmap.Lock())
                for (var y = 0; y < scene.Height; y++)
                    Marshal.Copy(pixels, y * scene.Width * 4, buffer.Address + y * buffer.RowBytes, scene.Width * 4);
            context.DrawImage(bitmap, new Rect(0, 0, scene.Width, scene.Height));
        }
        if (!textVisible || !frame.TextVisible) return;
        foreach (var obj in frame.TextObjects)
        {
            if (obj.Id == hiddenTextId) continue;
            var placement = obj.Placement(language, scene.FallbackLanguage);
            var text = scene.Text(language, obj.Key);
            var missing = string.IsNullOrWhiteSpace(text);
            var font = CutsceneFonts.Resolve(obj.FontId, language);
            var rendered = scene.RenderText(language, obj.Key);
            var formatted = new FormattedText(rendered,
                Culture(language), Culture(language).TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                new Typeface(font, obj.Italic ? FontStyle.Italic : FontStyle.Normal,
                    obj.Bold ? FontWeight.Bold : FontWeight.Normal), obj.FontSize, brushes[obj.Color])
            {
                MaxTextWidth = placement.Width
            };
            var styleLanguage = missing ? scene.FallbackLanguage : language;
            TextStyleFormatter.Apply(formatted, obj, styleLanguage, rendered.Length);
            var overflow = formatted.Height > placement.Height + 0.5 || formatted.Width > placement.Width + 0.5;
            TextEffectsRenderer.Draw(context, obj, placement, rendered, language, styleLanguage, brushes[obj.Color]);
            if (showBounds)
            {
                var outline = missing ? Brushes.OrangeRed : overflow ? Brushes.Red :
                    selectedId == obj.Id ? Brushes.DodgerBlue : Brushes.Transparent;
                if (outline != Brushes.Transparent)
                    context.DrawRectangle(null, new Pen(outline, 1), new Rect(placement.X, placement.Y, placement.Width, placement.Height));
                if (selectedId == obj.Id)
                    foreach (var handle in TextHandles(placement))
                        context.DrawRectangle(Brushes.White, new Pen(Brushes.DodgerBlue, .7), new Rect(handle.X - 2, handle.Y - 2, 4, 4));
                if (missing && string.IsNullOrWhiteSpace(rendered))
                {
                    var marker = new FormattedText("Missing translation", Culture(language), Culture(language).TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                        new Typeface(FontFamily.Default), Math.Min(10, obj.FontSize), Brushes.OrangeRed);
                    using (context.PushClip(new Rect(placement.X, placement.Y, placement.Width, placement.Height)))
                        context.DrawText(marker, new Point(placement.X + 3, placement.Y + 3));
                }
            }
        }
    }

    public static Point[] TextHandles(TextObject obj) => TextHandles(new TextPlacement { X = obj.X, Y = obj.Y, Width = obj.Width, Height = obj.Height });
    public static Point[] TextHandles(TextPlacement obj) =>
    [
        new(obj.X, obj.Y), new(obj.X + obj.Width / 2, obj.Y), new(obj.X + obj.Width, obj.Y),
        new(obj.X + obj.Width, obj.Y + obj.Height / 2), new(obj.X + obj.Width, obj.Y + obj.Height),
        new(obj.X + obj.Width / 2, obj.Y + obj.Height), new(obj.X, obj.Y + obj.Height), new(obj.X, obj.Y + obj.Height / 2)
    ];

    public static CultureInfo Culture(string code)
    {
        try { return CultureInfo.GetCultureInfo(code); }
        catch (CultureNotFoundException) { return CultureInfo.InvariantCulture; }
    }
}
