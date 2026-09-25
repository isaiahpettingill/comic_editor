using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ComicEditor.Editing;
using ComicEditor.Format;
using ComicEditor.Rendering;

namespace ComicEditor.Views;

public partial class MainView
{
    private ProjectFile? currentFile;
    private string? currentBookmark;
    private readonly SemaphoreSlim sessionLock = new(1, 1);
    private DispatcherTimer? sessionTimer;
    private bool sessionStarted, sessionReady, sessionRestored, fileBusy, autoSavePaused, closingAfterRecovery;
    private bool recoveryBlocked;
    private DateTimeOffset lastAutoSave = DateTimeOffset.UtcNow;
    private string? lastSessionJson;
    private string saveMessage = "Crash recovery keeps a copy every 5 seconds while you work.";
    private TextBlock? autosaveStatus;
    private MenuItem? autosaveMenu;

    private async void StartSession(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (sessionStarted) return;
        sessionStarted = true;
        if (SessionStorage.Enabled)
        {
            IsEnabled = false;
            try
            {
                var snapshot = SessionSnapshot.Parse(await SessionStorage.Read());
                if (snapshot is not null) { await RestoreSession(snapshot); sessionRestored = true; }
            }
            catch (Exception ex)
            {
                recoveryBlocked = true;
                SetSaveMessage("Recovery could not be loaded; the original snapshot was kept. Save or open a project to resume recovery. " + ex.Message, true);
            }
            finally { IsEnabled = true; }
            sessionReady = true;
            SessionStorage.Flush = FlushSession;
            sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            sessionTimer.Tick += async (_, _) => await SessionTick(); sessionTimer.Start();
            if (TopLevel.GetTopLevel(this) is Window window) window.Closing += ClosingSession;
        }
        activationReady = true;
        await OpenPendingFiles();
        StartUpdates();
    }

    private async Task RestoreSession(SessionSnapshot snapshot)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        IStorageFile? file = null;
        if (storage is not null)
        {
            try { if (snapshot.Bookmark is not null) file = await storage.OpenFileBookmarkAsync(snapshot.Bookmark); }
            catch (Exception) { /* A local path can still work when a bookmark is stale. */ }
            try { if (file is null && snapshot.LocalPath is not null) file = await storage.TryGetFileFromPathAsync(snapshot.LocalPath); }
            catch (Exception) { /* Restore the independent recovery copy if access was revoked. */ }
        }
        snapshot.Restore(editor);
        currentBookmark = snapshot.Bookmark;
        currentFile = file is null ? null : new ProjectFile(file, snapshot.DiskHash);
        if (!snapshot.Dirty && currentFile is not null)
        {
            try
            {
                var latest = await currentFile.Read(); editor.Load(latest, file!.Name);
                editor.SelectFrame(Math.Clamp(snapshot.Frame, 0, editor.Scene.Frames.Count - 1));
                currentFile = new ProjectFile(file, ProjectFile.Hash(latest));
            }
            catch (Exception) { currentFile = null; editor.MarkUnsaved(); }
        }
        if (currentFile is null && snapshot.FileName is not null) editor.MarkUnsaved();
        SetSaveMessage(snapshot.Dirty ? "Recovered unsaved edits from your last session." : "Reopened your last cutscene.");
        Build(compact);
        try { await CutsceneFonts.EnsureAsync(editor.Scene); RefreshCanvas(); QueueFontCheck(); }
        catch (Exception ex) { SetSaveMessage("Cutscene recovered; font loading failed: " + ex.Message, true); }
    }

    private async Task BindFile(IStorageFile file, byte[] bytes)
    {
        currentFile = new ProjectFile(file, ProjectFile.Hash(bytes)); currentBookmark = null;
        recoveryBlocked = false;
        try { if (file.CanBookmark) currentBookmark = await file.SaveBookmarkAsync(); }
        catch (Exception) { /* Local paths and the recovery copy remain available. */ }
        autoSavePaused = false; lastAutoSave = DateTimeOffset.UtcNow;
    }

    private void ClearFile()
    {
        currentFile = null; currentBookmark = null; autoSavePaused = false;
        recoveryBlocked = false;
        lastAutoSave = DateTimeOffset.UtcNow;
        SetSaveMessage("Choose Save to give this cutscene a file. Crash recovery is active.");
    }

    private async Task SaveSession(byte[]? project = null)
    {
        if (!sessionReady || !SessionStorage.Enabled || recoveryBlocked) return;
        await sessionLock.WaitAsync();
        try
        {
            var snapshot = SessionSnapshot.Capture(editor, project);
            snapshot.Bookmark = currentBookmark; snapshot.LocalPath = currentFile?.File.TryGetLocalPath(); snapshot.DiskHash = currentFile?.DiskHash;
            var json = await Task.Run(snapshot.Serialize); if (json == lastSessionJson) return;
            await SessionStorage.Write(json); lastSessionJson = json;
        }
        finally { sessionLock.Release(); }
    }

    private async Task SaveSessionSafely(byte[]? project = null)
    {
        try { await SaveSession(project); }
        catch (Exception ex) { SetSaveMessage("Recovery could not be saved: " + ex.Message, true); }
    }

    private Task FlushSession() => SaveSessionSafely();

    private async Task SessionTick()
    {
        await OpenPendingFiles();
        if (fileBusy || updateInstalling || dragging || pathBase is not null || cancelTouchEdit is not null) return;
        // Browser save handles may represent downloads, requiring a user gesture each time.
        if (editor.Preferences.AutoSave && !autoSavePaused && currentFile is not null && !OperatingSystem.IsBrowser() &&
            DateTimeOffset.UtcNow - lastAutoSave >= TimeSpan.FromMinutes(editor.Preferences.AutoSaveMinutes))
            await SaveCurrent(automatic: true);
        else await SaveSessionSafely();
    }

    private async Task SaveCurrent(bool automatic)
    {
        if (currentFile is null || fileBusy) return;
        var file = currentFile; var scene = editor.Scene; var bytes = CutsceneFile.Write(scene);
        if (automatic && !editor.DiffersFromSaved(bytes)) { await SaveSessionSafely(bytes); return; }
        fileBusy = true;
        try
        {
            // Recovery and the project file have independent destinations.
            var recovery = SaveSessionSafely(bytes);
            try { await file.Write(bytes, checkExternalChanges: true); }
            finally { await recovery; }
            if (ReferenceEquals(editor.Scene, scene) && ReferenceEquals(currentFile, file)) editor.MarkSaved(bytes);
            lastAutoSave = DateTimeOffset.UtcNow; autoSavePaused = false;
            SetSaveMessage($"{(automatic ? "Autosaved" : "Saved")} {file.File.Name} at {DateTime.Now:t}.");
            RefreshTitle(); // The periodic recovery tick will mark this snapshot clean.
        }
        catch (Exception ex)
        {
            autoSavePaused = true;
            SetSaveMessage("Autosave paused: " + ex.Message, true);
            if (!automatic) await ShowError(ex.Message);
        }
        finally { fileBusy = false; }
    }

    private void SetSaveMessage(string message, bool error = false)
    {
        saveMessage = message;
        if (autosaveStatus is not null) autosaveStatus.Text = message;
        if (autosaveMenu is not null) autosaveMenu.Header = error ? "Autosave & recovery • Attention…" : "Autosave & recovery…";
        if (compactDrawer is not null && error) compactDrawer.Foreground = Brush(UiTheme.Error);
    }

    private void ShowAutosave()
    {
        var enabled = new CheckBox { Name = "EnableAutosave", Content = "Automatically save my file", IsChecked = editor.Preferences.AutoSave, IsEnabled = !OperatingSystem.IsBrowser() };
        var interval = new NumericUpDown { Name = "AutosaveInterval", Minimum = 1, Maximum = 60, Value = editor.Preferences.AutoSaveMinutes };
        autosaveStatus = Label(saveMessage); autosaveStatus.Name = "AutosaveStatus";
        ShowModal("Autosave & recovery", new StackPanel
        {
            Spacing = 10,
            Children =
            {
                enabled, Label("Save interval (minutes)"), interval,
                Label(OperatingSystem.IsBrowser()
                    ? "This browser keeps a recovery copy in local storage every 5 seconds. Use Save to download your cutscene; automatic downloads are disabled."
                    : "Choose Save once to select a file. Autosave then saves your edits to that file at this interval."),
                Label("Your last cutscene and unsaved edits reopen on startup, even with autosave off. Recovery is updated every 5 seconds between strokes. Undo history resets after reopening."),
                autosaveStatus
            }
        }, () =>
        {
            editor.Preferences.AutoSave = enabled.IsChecked == true;
            editor.Preferences.AutoSaveMinutes = Math.Clamp((int)(interval.Value ?? 2), 1, 60);
            editor.Preferences.Save(); lastAutoSave = DateTimeOffset.UtcNow; CloseModal();
        });
    }

    private async void ClosingSession(object? sender, WindowClosingEventArgs args)
    {
        if (closingAfterRecovery || updateInstalling || sender is not Window window) return;
        args.Cancel = true;
        FinishPath(); StopSpray();
        IsEnabled = false;
        await SaveSessionSafely();
        closingAfterRecovery = true; window.Close();
    }
}
