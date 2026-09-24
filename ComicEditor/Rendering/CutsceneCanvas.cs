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
    public bool TextVisible { get; set; } = true;
    public Rect? DraftTextBounds { get; set; }

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
            RenderFrame(context, Scene, FrameIndex, Language, TextVisible, ShowTextBounds, SelectedTextId, OnionSkin);
            if (DraftTextBounds is Rect draft)
                context.DrawRectangle(null, new Pen(Brushes.DodgerBlue, 1), draft);
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
        bool textVisible = true, bool showBounds = false, string? selectedId = null, bool onionBackground = false)
    {
        var frame = scene.Frames[frameIndex];
        var brushes = scene.Palette.Select(hex => new SolidColorBrush(Color.Parse(hex))).ToArray();
        // Composite indices before scaling. Adjacent antialiased rectangles leave seams
        // at fractional zoom; a single nearest-neighbor bitmap preserves solid pixels.
        var pixels = new byte[scene.Width * scene.Height * 4];
        var colors = scene.Palette.Select(Color.Parse).ToArray();
        foreach (var layer in frame.Layers.Where(l => l.Visible))
        {
            for (var y = 0; y < scene.Height; y++)
            {
                var row = layer.Rows[y];
                for (var x = 0; x < scene.Width; x++)
                {
                    var color = Convert.ToInt32(row.Substring(x * 2, 2), 16);
                    if (color == 255) continue;
                    var offset = (y * scene.Width + x) * 4;
                    pixels[offset] = colors[color].B; pixels[offset + 1] = colors[color].G;
                    pixels[offset + 2] = colors[color].R; pixels[offset + 3] = 255;
                }
            }
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
            var text = scene.Text(language, obj.Key);
            var missing = string.IsNullOrWhiteSpace(text);
            var font = CutsceneFonts.Resolve(obj.FontId);
            var rendered = scene.RenderText(language, obj.Key);
            var formatted = new FormattedText(rendered,
                Culture(language), Culture(language).TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                new Typeface(font, obj.Italic ? FontStyle.Italic : FontStyle.Normal,
                    obj.Bold ? FontWeight.Bold : FontWeight.Normal), obj.FontSize, brushes[obj.Color])
            {
                MaxTextWidth = obj.Width
            };
            var overflow = formatted.Height > obj.Height + 0.5 || formatted.Width > obj.Width + 0.5;
            using (context.PushClip(new Rect(obj.X, obj.Y, obj.Width, obj.Height)))
                context.DrawText(formatted, new Point(obj.X, obj.Y));
            if (showBounds)
            {
                var outline = missing ? Brushes.OrangeRed : overflow ? Brushes.Red :
                    selectedId == obj.Id ? Brushes.DodgerBlue : Brushes.Transparent;
                if (outline != Brushes.Transparent)
                    context.DrawRectangle(null, new Pen(outline, 1), new Rect(obj.X, obj.Y, obj.Width, obj.Height));
                if (selectedId == obj.Id)
                    foreach (var handle in TextHandles(obj))
                        context.DrawRectangle(Brushes.White, new Pen(Brushes.DodgerBlue, .7), new Rect(handle.X - 2, handle.Y - 2, 4, 4));
                if (missing && string.IsNullOrWhiteSpace(rendered))
                {
                    var marker = new FormattedText("Missing translation", Culture(language), Culture(language).TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                        new Typeface(FontFamily.Default), Math.Min(10, obj.FontSize), Brushes.OrangeRed);
                    using (context.PushClip(new Rect(obj.X, obj.Y, obj.Width, obj.Height)))
                        context.DrawText(marker, new Point(obj.X + 3, obj.Y + 3));
                }
            }
        }
    }

    public static Point[] TextHandles(TextObject obj) =>
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
