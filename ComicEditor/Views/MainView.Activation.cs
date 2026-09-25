using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ComicEditor.Editing;

namespace ComicEditor.Views;

public partial class MainView
{
    private readonly Queue<Func<Task<IStorageFile?>>> pendingFiles = new();
    private bool activationReady, activationBusy;

    public void QueueOpenPath(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.IsFile) path = uri.LocalPath;
        if (!ProjectTypes.IsProject(path)) return;
        pendingFiles.Enqueue(async () => await (TopLevel.GetTopLevel(this)?.StorageProvider.TryGetFileFromPathAsync(Path.GetFullPath(path)) ?? Task.FromResult<IStorageFile?>(null)));
        Dispatcher.UIThread.Post(() => _ = OpenPendingFiles());
    }

    public void QueueOpenFile(IStorageFile file)
    {
        pendingFiles.Enqueue(() => Task.FromResult<IStorageFile?>(file));
        Dispatcher.UIThread.Post(() => _ = OpenPendingFiles());
    }

    private async Task OpenPendingFiles()
    {
        if (!activationReady || activationBusy || fileBusy || modal is not null || pendingFiles.Count == 0) return;
        activationBusy = true;
        try
        {
            var file = await pendingFiles.Dequeue()() ?? throw new IOException("The cutscene could not be opened. Check that the file still exists and is accessible.");
            if (!ProjectTypes.IsProject(file.Name)) throw new InvalidDataException("Choose a .ctsc or .cutscene project.");
            if (TopLevel.GetTopLevel(this) is Window window) { if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal; window.Activate(); }
            await OpenProjectFile(file);
        }
        catch (Exception ex) { await ShowError(ex.Message); }
        finally { activationBusy = false; }
    }
}
