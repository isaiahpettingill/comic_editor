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
        if (!RgbaColor.IsHex(hex) || hex.Length != (editor.Scene.IsRgba ? 9 : 7) ||
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
            var existing = tabs.FirstOrDefault(tab => string.Equals((tab == activeTab ? currentFile : tab.File)?.File.TryGetLocalPath(), file.TryGetLocalPath(),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) && file.TryGetLocalPath() is not null);
            if (existing is not null) { SwitchTab(existing, force: true); return; }
            var opened = new EditorState(editor.Preferences);
            opened.Load(bytes, file.Name);
            AddTab(opened); await BindFile(file, bytes); Build(compact);
            selection = null;
            SetSaveMessage($"Opened {file.Name}. Crash recovery is active.");
            await SaveSessionSafely();
            await CutsceneFonts.EnsureAsync(editor.Scene); RefreshAll();
        }
        catch (Exception ex) { await ShowError(ex.Message); }
        finally { fileBusy = false; }
    }

    private bool savePickerOpen, saveAsQueued;

    private Task Save() => QueueManualSave(saveAs: false);

    private Task SaveAs() => QueueManualSave(saveAs: true);

    private async Task QueueManualSave(bool saveAs)
    {
        if (savePickerOpen) return;
        var needsPicker = saveAs || currentFile is null || OperatingSystem.IsBrowser();
        if (needsPicker && saveAsQueued) return;
        if (needsPicker) saveAsQueued = true;
        var requestedTab = activeTab;
        await fileSaveLock.WaitAsync();
        try
        {
            // Don't save a different document if another operation replaced it
            // while this keyboard request was waiting for the previous write.
            if (fileBusy || activeTab != requestedTab) return;
            FinishPath();
            if (!saveAs && currentFile is not null && !OperatingSystem.IsBrowser())
                await SaveCurrentCore(automatic: false);
            else await SaveAsCore();
        }
        finally
        {
            if (needsPicker) saveAsQueued = false;
            fileSaveLock.Release();
        }
    }

    private async Task SaveAsCore()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider; if (storage is null) return;
        // Reserve the document before awaiting the picker, not after it. Cancel
        // leaves the old file binding and saved revision untouched.
        fileBusy = true; savePickerOpen = true;
        var state = editor;
        try
        {
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save cutscene",
                SuggestedFileName = state.FileName ?? "cutscene.ctsc",
                DefaultExtension = state.FileName is { } name && Path.GetExtension(name).Equals(".cutscene", StringComparison.OrdinalIgnoreCase) ? "cutscene" : "ctsc",
                FileTypeChoices = [ProjectTypes.Editable]
            });
            savePickerOpen = false;
            if (file is null) return;
            var capture = CaptureProject(state);
            var bytes = await capture.Bytes;
            await new ProjectFile(file, null).Write(bytes, checkExternalChanges: false);
            if (ReferenceEquals(editor, state) && ReferenceEquals(state.Scene, capture.Scene))
            {
                await BindFile(file, bytes); state.FileName = file.Name; state.MarkSaved(bytes, capture.Stable ? capture.Revision : long.MinValue);
                SetSaveMessage($"Saved {file.Name} at {DateTime.Now:t}."); RefreshTitle();
            }
            _ = SaveSessionSafely();
        }
        catch (Exception ex) { await ShowError(ex.Message); }
        finally { fileBusy = false; savePickerOpen = false; }
    }

    private async Task ExportDisplay(bool cbor = false)
    {
        FinishPath();
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider; if (storage is null) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = cbor ? "Build CBOR game cutscene" : "Build game cutscene",
            SuggestedFileName = cbor ? "cutscene.cbor" : "cutscene.cutscene.runtime",
            DefaultExtension = cbor ? "cbor" : "runtime",
            FileTypeChoices = [new FilePickerFileType(cbor ? "CBOR cutscene" : "Compiled cutscene") { Patterns = [cbor ? "*.cbor" : "*.cutscene.runtime"] }]
        });
        if (file is null) return;
        try
        {
            await CutsceneFonts.EnsureAsync(editor.Scene);
            var bytes = cbor ? CborExporter.Compile(editor.Scene) : DisplayCompiler.Compile(editor.Scene).ToByteArray();
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
            Label("Smooth text edges, white background, and only the colors visible in each image. The preview language stays unchanged.")
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
