using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using ComicEditor.Editing;
using ComicEditor.Format;
using ComicEditor.Rendering;
using SkiaSharp;

namespace ComicEditor.Views;

public partial class MainView
{
    private bool importingImage;
    private const int MaximumImageBytes = 64 * 1024 * 1024;
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif" };
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase) { ".wav", ".qoa", ".mp3", ".ogg", ".flac", ".m4a", ".aac", ".opus", ".aiff", ".aif", ".webm" };

    private void AttachMediaDrop()
    {
        if (rootGrid is null) return;
        DragDrop.SetAllowDrop(rootGrid, true);
        rootGrid.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            var files = e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().ToArray();
            var valid = modal is null && files is { Length: 1 } &&
                (ImageExtensions.Contains(Path.GetExtension(files[0].Name)) || AudioExtensions.Contains(Path.GetExtension(files[0].Name)) && DropFrame(e.Source) is not null);
            e.DragEffects = valid ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true;
        });
        rootGrid.AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            if (modal is not null) return;
            var files = e.DataTransfer.TryGetFiles()?.OfType<IStorageFile>().ToArray();
            if (files is not { Length: 1 }) { await ShowError("Drop one image, or one audio file onto a frame."); return; }
            var file = files[0]; var extension = Path.GetExtension(file.Name); var scene = editor.Scene;
            var frame = DropFrame(e.Source) ?? editor.Frame; e.Handled = true;
            if (AudioExtensions.Contains(extension))
            {
                if (DropFrame(e.Source) is null) { await ShowError("Drop audio onto its storyboard frame."); return; }
                await ImportFrameAudio(file, scene, frame); return;
            }
            if (!ImageExtensions.Contains(extension)) return;
            try
            {
                await using var stream = await file.OpenReadAsync();
                var bytes = await ReadImageBytes(stream);
                var point = canvas is null || DropFrame(e.Source) is not null ? default : e.GetPosition(canvas);
                await ImportImage(bytes, file.Name, scene, frame, Math.Max(0, (int)point.X), Math.Max(0, (int)point.Y));
            }
            catch (Exception ex) { await ShowError(ex.Message); }
        });
    }

    private Frame? DropFrame(object? source)
    {
        var index = FocusedFrameIndex(source);
        return index is int value && value < editor.Scene.Frames.Count ? editor.Scene.Frames[value] : null;
    }

    private static async Task<byte[]> ReadImageBytes(Stream stream)
    {
        using var buffer = new MemoryStream(); var bytes = new byte[81920]; int read;
        while ((read = await stream.ReadAsync(bytes)) > 0)
        {
            if (buffer.Length + read > MaximumImageBytes) throw new InvalidDataException("Image exceeds 64 MiB.");
            buffer.Write(bytes, 0, read);
        }
        return buffer.ToArray();
    }

    private async Task MarkInternalClipboard()
    {
        // A real Copy operation replaces the system clipboard. This also prevents
        // an old external image from overriding a newly copied internal frame.
        try { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync("ComicEditor internal artwork"); }
        catch { /* Internal clipboard remains available when the platform denies clipboard writes. */ }
    }

    private async Task<bool> TryPasteExternalImage()
    {
        if (importingImage) return true;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard; if (clipboard is null) return false;
        var scene = editor.Scene; var frame = editor.Frame;
        try
        {
            using var bitmap = await clipboard.TryGetBitmapAsync();
            if (bitmap is null) return false;
            if ((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height > 16_777_216)
                throw new InvalidDataException("Clipboard image is larger than 16 megapixels.");
            using var buffer = new MemoryStream(); bitmap.Save(buffer, PngBitmapEncoderOptions.Default); var bytes = buffer.ToArray();
            await ImportImage(bytes, "Pasted image", scene, frame, 0, 0); return true;
        }
        catch (Exception ex) { await ShowError("Could not paste the image: " + ex.Message); return true; }
    }

    private async Task ImportImage(byte[] bytes, string name, Cutscene scene, Frame frame, int x, int y)
    {
        if (importingImage || bytes.Length > MaximumImageBytes || editor.Scene != scene) return;
        importingImage = true;
        var revision = editor.Revision; var paletteCopy = scene.Palette.ToList();
        var width = scene.Width; var height = scene.Height; var rgba = scene.IsRgba;
        try
        {
            var layer = await Task.Run(() =>
            {
                using var data = SKData.CreateCopy(bytes);
                using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Unsupported or corrupt image.");
                if (codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 16_777_216)
                    throw new InvalidDataException("Image is larger than 16 megapixels. Resize it before importing.");
                using var bitmap = SKBitmap.Decode(codec) ?? throw new InvalidDataException("Could not decode image.");
                var target = ArtworkLayer.Create(Path.GetFileNameWithoutExtension(name), width, height, rgba);
                var transfer = new PaletteTransfer([], paletteCopy, rgba);
                x = Math.Clamp(x, 0, Math.Max(0, width - Math.Min(width, bitmap.Width)));
                y = Math.Clamp(y, 0, Math.Max(0, height - Math.Min(height, bitmap.Height)));
                for (var row = 0; row < Math.Min(bitmap.Height, height - y); row++)
                {
                    for (var col = 0; col < Math.Min(bitmap.Width, width - x); col++)
                    {
                        var color = bitmap.GetPixel(col, row); if (color.Alpha == 0) continue;
                        var packed = ((uint)color.Red << 24) | ((uint)color.Green << 16) | ((uint)color.Blue << 8) | color.Alpha;
                        if (rgba) target.SetRgbaPixel(col + x, row + y, packed);
                        else target.SetPixel(col + x, row + y, transfer.MapColor(packed));
                    }
                }
                return target;
            });
            if (editor.Scene != scene || !scene.Frames.Contains(frame) || editor.Revision != revision)
            { SetSaveMessage("Image import cancelled because the document changed. Paste or drop again."); return; }
            FinishPath(); editor.BeforeChange(); scene.Palette = paletteCopy; frame.Layers.Add(layer);
            if (editor.Frame == frame) { editor.LayerIndex = frame.Layers.Count - 1; editor.Tool = Tool.Pixel; }
            editor.RememberPalette(); RefreshAll(); RefreshTools();
            SetSaveMessage("Image added on a new layer. Pixels outside the canvas are cropped; Undo removes the import.");
        }
        finally { importingImage = false; }
    }

    private async Task PrintCurrentFrame()
    {
        if (OperatingSystem.IsBrowser() || OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
        { await Export(false); SetSaveMessage("PNG exported. Open it in your image viewer and choose Print."); return; }
        try
        {
            FinishPath(); var scene = editor.Scene.Snapshot(); var index = editor.FrameIndex; var language = editor.Language;
            await CutsceneFonts.EnsureAsync(scene);
            using var png = new MemoryStream(); await Task.Run(() => PngExporter.Write(png, scene, index, language));
            var folder = Directory.CreateTempSubdirectory("comic-print-"); var path = Path.Combine(folder.FullName, "frame.html");
            var html = "<!doctype html><html><head><title>ComicEditor — Print frame</title><style>img{max-width:100%;max-height:95vh}button{margin:1em}@media print{button{display:none}}</style></head><body><button onclick='window.print()'>Print frame</button><br><img alt='Current cutscene frame' src='data:image/png;base64," + Convert.ToBase64String(png.ToArray()) + "' onload='window.print()'></body></html>";
            await File.WriteAllTextAsync(path, html);
            await ComicEditor.Printing.PrintPreviewLauncher.OpenAsync(path);
            SetSaveMessage("Sent the frame preview to your browser. Choose Print frame if no dialog appears; nothing prints until you confirm.");
        }
        catch (Exception ex) { await ShowError("Could not open printing. Use File → Export frame PNG instead. " + ex.Message); }
    }
}
