using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ComicEditor.Audio;
using ComicEditor.Format;

namespace ComicEditor.Views;

public partial class MainView
{
    private CancellationTokenSource? audioImport;
    private bool choosingAudio;

    private async Task AttachFrameAudio(int index)
    {
        if (fileBusy || audioImport is not null || choosingAudio) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider; if (storage is null) return;
        var scene = editor.Scene; var frame = scene.Frames[index]; var previous = frame.AudioQoa;
        IReadOnlyList<IStorageFile> files;
        choosingAudio = true;
        try { files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Attach frame audio (WAV, QOA, or other audio with FFmpeg)", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Audio") { Patterns = ["*.wav", "*.qoa", "*.mp3", "*.ogg", "*.flac", "*.m4a", "*.aac", "*.opus", "*.aiff"] }, FilePickerFileTypes.All]
        }); }
        catch (Exception ex) { if (editor.Scene == scene && modal is null) await ShowError(ex.Message); return; }
        finally { choosingAudio = false; }
        if (files.Count == 0 || editor.Scene != scene || !scene.Frames.Contains(frame)) return;
        await ImportFrameAudio(files[0], scene, frame);
    }

    private async Task ImportFrameAudio(IStorageFile file, Cutscene scene, Frame frame)
    {
        if (audioImport is not null || editor.Scene != scene || !scene.Frames.Contains(frame)) return;
        var previous = frame.AudioQoa;
        using var cancellation = new CancellationTokenSource(); audioImport = cancellation;
        ShowModal("Importing frame audio", Label("Encoding QOA audio… Close or Escape cancels without changing the frame."));
        var progress = modal;
        try
        {
            await using var stream = await file.OpenReadAsync();
            using var buffer = new MemoryStream(); var chunk = new byte[81920]; int read;
            while ((read = await stream.ReadAsync(chunk, cancellation.Token)) > 0)
            {
                if (buffer.Length + read > AudioImporter.MaximumInputBytes) throw new InvalidDataException("Audio input exceeds 256 MiB.");
                buffer.Write(chunk, 0, read);
            }
            var qoa = await AudioImporter.ImportAsync(buffer.ToArray(), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (editor.Scene != scene || !scene.Frames.Contains(frame) || !ReferenceEquals(frame.AudioQoa, previous) || !ReferenceEquals(modal, progress)) return;
            FinishPath(); editor.BeforeChange(); frame.AttachAudio(qoa);
            if (ReferenceEquals(modal, progress)) CloseModal(); RefreshAll();
            SetSaveMessage($"Attached QOA audio ({frame.MinimumDurationMs / 1000.0:0.###}s). Frame duration: {frame.DurationMs / 1000.0:0.###}s.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested && ReferenceEquals(modal, progress))
            { CloseModal(); await ShowError(ex.Message); }
        }
        finally
        {
            if (ReferenceEquals(modal, progress)) CloseModal();
            if (ReferenceEquals(audioImport, cancellation)) audioImport = null;
        }
    }

    private void RemoveFrameAudio(int index)
    {
        var frame = editor.Scene.Frames[index]; if (frame.AudioQoa.Length == 0) return;
        FinishPath(); editor.BeforeChange(); frame.RemoveAudio(); RefreshAll();
    }
}
