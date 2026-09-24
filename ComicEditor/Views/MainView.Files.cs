using Avalonia;
using Google.Protobuf;
using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using ComicEditor.Format;
using ComicEditor.Rendering;

namespace ComicEditor.Views;

public partial class MainView
{
    private void SetPaletteColor(int index, string hex)
    {
        hex = hex.Trim().ToUpperInvariant();
        if (hex.Length != 7 || hex[0] != '#' || !hex[1..].All(Uri.IsHexDigit) ||
            hex == editor.Scene.Palette[index]) return;
        editor.BeforeChange(); editor.Scene.Palette[index] = hex; RefreshAll();
    }

    private async Task Open()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider; if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open cutscene",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Cutscene") { Patterns = ["*.cutscene"] }]
        });
        if (files.Count == 0) return;
        try
        {
            await using var stream = await files[0].OpenReadAsync(); using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer); editor.Load(buffer.ToArray(), files[0].Name); Build(compact);
            await CutsceneFonts.EnsureAsync(editor.Scene); RefreshAll();
        }
        catch (Exception ex) { await ShowError(ex.Message); }
    }

    private async Task Save()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider; if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save cutscene",
            SuggestedFileName = editor.FileName ?? "cutscene.cutscene",
            DefaultExtension = "cutscene",
            FileTypeChoices = [new FilePickerFileType("Cutscene") { Patterns = ["*.cutscene"] }]
        });
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            if (stream.CanSeek) stream.SetLength(0);
            await stream.WriteAsync(CutsceneFile.Write(editor.Scene));
            editor.FileName = file.Name; editor.MarkSaved(); RefreshTitle();
        }
        catch (Exception ex) { await ShowError(ex.Message); }
    }

    private async Task ExportDisplay()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider; if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Build game cutscene",
            SuggestedFileName = "cutscene.cutscene.runtime",
            DefaultExtension = "runtime",
            FileTypeChoices = [new FilePickerFileType("Compiled cutscene") { Patterns = ["*.cutscene.runtime"] }]
        });
        if (file is null) return;
        try
        {
            await CutsceneFonts.EnsureAsync(editor.Scene);
            var bytes = DisplayCompiler.Compile(editor.Scene).ToByteArray();
            await using var stream = await file.OpenWriteAsync(); if (stream.CanSeek) stream.SetLength(0);
            await stream.WriteAsync(bytes);
        }
        catch (Exception ex) { await ShowError(ex.Message); }
    }

    private async Task Export(bool all)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider; if (storage is null) return;
        if (all)
        {
            if (OperatingSystem.IsBrowser() || !storage.CanPickFolder)
            {
                var archive = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export all frames",
                    SuggestedFileName = $"frames-{editor.Language}.zip",
                    DefaultExtension = "zip",
                    FileTypeChoices = [new FilePickerFileType("PNG archive") { Patterns = ["*.zip"] }]
                });
                if (archive is null) return;
                try
                {
                    await using var output = await archive.OpenWriteAsync();
                    using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
                    for (var i = 0; i < editor.Scene.Frames.Count; i++)
                    {
                        using var entry = zip.CreateEntry($"frame-{i + 1:D4}-{editor.Language}.png").Open();
                        RenderPng(entry, i);
                    }
                }
                catch (Exception ex) { await ShowError(ex.Message); }
                return;
            }
            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Export frames", AllowMultiple = false });
            if (folders.Count == 0) return;
            try
            {
                for (var i = 0; i < editor.Scene.Frames.Count; i++)
                {
                    var file = await folders[0].CreateFileAsync($"frame-{i + 1:D4}-{editor.Language}.png"); if (file is null) continue;
                    await using var stream = await file.OpenWriteAsync(); RenderPng(stream, i);
                }
            }
            catch (Exception ex) { await ShowError(ex.Message); }
        }
        else
        {
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export frame",
                SuggestedFileName = $"frame-{editor.FrameIndex + 1:D4}-{editor.Language}.png",
                DefaultExtension = "png",
                FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }]
            });
            if (file is null) return;
            try { await using var stream = await file.OpenWriteAsync(); RenderPng(stream, editor.FrameIndex); }
            catch (Exception ex) { await ShowError(ex.Message); }
        }
    }

    private void RenderPng(Stream stream, int frame)
    {
        var view = new CutsceneCanvas { Scene = editor.Scene, FrameIndex = frame, Language = editor.Language, Width = editor.Scene.Width, Height = editor.Scene.Height };
        view.Measure(new Size(view.Width, view.Height)); view.Arrange(new Rect(view.DesiredSize));
        using var bitmap = new RenderTargetBitmap(new PixelSize(editor.Scene.Width, editor.Scene.Height));
        bitmap.Render(view); bitmap.Save(stream, PngBitmapEncoderOptions.Default);
    }

    private Task ShowError(string message)
    {
        ShowModal("Cutscene error", Label(message));
        return Task.CompletedTask;
    }
}
