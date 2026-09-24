using Avalonia;
using Google.Protobuf;
using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using ComicEditor.Format;
using ComicEditor.Rendering;
using ComicEditor.Editing;

namespace ComicEditor.Views;

public partial class MainView
{
    private void SetPaletteColor(int index, string hex)
    {
        hex = hex.Trim().ToUpperInvariant();
        if (hex.Length != 7 || hex[0] != '#' || !hex[1..].All(Uri.IsHexDigit) ||
            hex == editor.Scene.Palette[index]) return;
        editor.BeforeChange(); editor.Scene.Palette[index] = hex; editor.RememberPalette(); RefreshAll();
    }

    private async Task Open()
    {
        if (fileBusy) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider; if (storage is null) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open cutscene",
            AllowMultiple = false,
            FileTypeFilter = [ProjectTypes.Editable]
        });
        if (files.Count == 0) return;
        QueueOpenFile(files[0]);
    }

    private async Task OpenProjectFile(IStorageFile file)
    {
        if (fileBusy) return;
        FinishPath();
        fileBusy = true;
        try
        {
            await using var stream = await file.OpenReadAsync(); using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer); var bytes = buffer.ToArray();
            editor.Load(bytes, file.Name); await BindFile(file, bytes); Build(compact);
            selection = clipboardSelection = null;
            SetSaveMessage($"Opened {file.Name}. Crash recovery is active.");
            await SaveSessionSafely();
            await CutsceneFonts.EnsureAsync(editor.Scene); RefreshAll();
        }
        catch (Exception ex) { await ShowError(ex.Message); }
        finally { fileBusy = false; }
    }

    private async Task Save()
    {
        if (fileBusy) return;
        FinishPath();
        if (currentFile is not null && !OperatingSystem.IsBrowser()) { await SaveCurrent(automatic: false); return; }
        await SaveAs();
    }

    private async Task SaveAs()
    {
        if (fileBusy) return;
        FinishPath();
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider; if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save cutscene",
            SuggestedFileName = editor.FileName ?? "cutscene.ctsc",
            DefaultExtension = editor.FileName is { } name && Path.GetExtension(name).Equals(".cutscene", StringComparison.OrdinalIgnoreCase) ? "cutscene" : "ctsc",
            FileTypeChoices = [ProjectTypes.Editable]
        });
        if (file is null) return;
        fileBusy = true;
        try
        {
            var scene = editor.Scene; var bytes = CutsceneFile.Write(scene);
            await SaveSessionSafely();
            await new ProjectFile(file, null).Write(bytes, checkExternalChanges: false);
            if (ReferenceEquals(editor.Scene, scene))
            {
                await BindFile(file, bytes); editor.FileName = file.Name; editor.MarkSaved(bytes);
                SetSaveMessage($"Saved {file.Name} at {DateTime.Now:t}."); RefreshTitle(); await SaveSessionSafely();
            }
        }
        catch (Exception ex) { await ShowError(ex.Message); }
        finally { fileBusy = false; }
    }

    private async Task ExportDisplay()
    {
        FinishPath();
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

    private Task Export(bool all)
    {
        FinishPath();
        var languages = editor.Scene.Translations.Keys.Order().ToArray();
        var language = new ComboBox { Name = "ExportLanguage", ItemsSource = languages, SelectedItem = editor.Language, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (language.SelectedIndex < 0 && languages.Length > 0) language.SelectedIndex = 0;
        ShowModal(all ? "Export all frames as indexed PNGs" : "Export indexed PNG", new StackPanel
        {
            Spacing = 10,
            Children =
        {
            Label("Language"), language,
            Label("Crisp text edges, white background, and only the colors visible in each image. The preview language stays unchanged.")
        }
        }, () =>
        {
            var chosen = language.SelectedItem as string ?? "und"; CloseModal(); _ = ExportPngFiles(all, chosen);
        }, "Export");
        return Task.CompletedTask;
    }

    private async Task ExportPngFiles(bool all, string language)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider; if (storage is null) return;
        try { await CutsceneFonts.EnsureAsync(editor.Scene); CutsceneFonts.RequireAvailable(editor.Scene, language); }
        catch (Exception ex) { await ShowError(ex.Message); return; }
        if (all)
        {
            if (OperatingSystem.IsBrowser() || !storage.CanPickFolder)
            {
                var archive = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export all frames",
                    SuggestedFileName = $"frames-{language}.zip",
                    DefaultExtension = "zip",
                    FileTypeChoices = [new FilePickerFileType("PNG archive") { Patterns = ["*.zip"] }]
                });
                if (archive is null) return;
                try
                {
                    await using var output = await archive.OpenWriteAsync();
                    if (output.CanSeek) output.SetLength(0);
                    using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
                    for (var i = 0; i < editor.Scene.Frames.Count; i++)
                    {
                        using var entry = zip.CreateEntry($"frame-{i + 1:D4}-{language}.png").Open();
                        RenderPng(entry, i, language);
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
                    var file = await folders[0].CreateFileAsync($"frame-{i + 1:D4}-{language}.png"); if (file is null) continue;
                    await using var stream = await file.OpenWriteAsync(); RenderPng(stream, i, language);
                }
            }
            catch (Exception ex) { await ShowError(ex.Message); }
        }
        else
        {
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export frame",
                SuggestedFileName = $"frame-{editor.FrameIndex + 1:D4}-{language}.png",
                DefaultExtension = "png",
                FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }]
            });
            if (file is null) return;
            try { await using var stream = await file.OpenWriteAsync(); RenderPng(stream, editor.FrameIndex, language); }
            catch (Exception ex) { await ShowError(ex.Message); }
        }
    }

    private void ExportBook(BookFormat format)
    {
        FinishPath();
        var languages = editor.Scene.Translations.Keys.Order().ToArray();
        var language = new ComboBox { Name = "ExportLanguage", ItemsSource = languages, SelectedItem = editor.Language, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (language.SelectedIndex < 0 && languages.Length > 0) language.SelectedIndex = 0;
        ShowModal($"Export {format.ToString().ToUpperInvariant()}", new StackPanel
        {
            Spacing = 10,
            Children = { Label("Language"), language, Label("One frame per page. Text is rendered in the selected language.") }
        }, () =>
        {
            var chosen = language.SelectedItem as string ?? "und";
            CloseModal(); _ = ExportBookFile(format, chosen);
        }, "Export");
    }

    private async Task ExportBookFile(BookFormat format, string language)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider; if (storage is null) return;
        var extension = format.ToString().ToLowerInvariant();
        try
        {
            await CutsceneFonts.EnsureAsync(editor.Scene);
            CutsceneFonts.RequireAvailable(editor.Scene, language);
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = $"Export {extension.ToUpperInvariant()}",
                SuggestedFileName = $"cutscene-{language}.{extension}",
                DefaultExtension = extension,
                FileTypeChoices = [new FilePickerFileType($"{extension.ToUpperInvariant()} book") { Patterns = [$"*.{extension}"] }]
            });
            if (file is null) return;
            await using var stream = await file.OpenWriteAsync(); if (stream.CanSeek) stream.SetLength(0);
            BookExporter.Write(stream, editor.Scene, language, format);
            SetSaveMessage($"Exported {file.Name}.");
        }
        catch (Exception ex) { await ShowError(ex.Message); }
    }

    private void RenderPng(Stream stream, int frame, string language)
    {
        if (stream.CanSeek) stream.SetLength(0);
        PngExporter.Write(stream, editor.Scene, frame, language);
    }

    private Task ShowError(string message)
    {
        ShowModal("Cutscene error", Label(message));
        return Task.CompletedTask;
    }
}
