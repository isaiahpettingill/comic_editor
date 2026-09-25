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
    private DispatcherTimer? idleSaveTimer;
    private bool sessionStarted, sessionReady, sessionRestored, fileBusy, autoSavePaused, closingAfterRecovery;
    private bool recoveryBlocked;
    private DateTimeOffset lastAutoSave = DateTimeOffset.UtcNow;
    private string? lastSessionJson;
    private string saveMessage = "Crash recovery keeps a copy every 5 seconds while you work.";
    private TextBlock? autosaveStatus;
    private MenuItem? autosaveMenu;

    private void TrackEditor(EditorState state)
    {
        state.Changed -= QueueIdleSave;
        state.Changed += QueueIdleSave;
    }

    private void QueueIdleSave()
    {
        if (!sessionReady) return;
        idleSaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        idleSaveTimer.Tick -= IdleSaveTick;
        idleSaveTimer.Tick += IdleSaveTick;
        idleSaveTimer.Stop(); idleSaveTimer.Start();
    }

    private async void IdleSaveTick(object? sender, EventArgs args)
    {
        idleSaveTimer?.Stop();
        if (dragging || pathBase is not null || cancelTouchEdit is not null || fileBusy || updateInstalling)
        { QueueIdleSave(); return; }
        if (editor.Preferences.AutoSave && !autoSavePaused && currentFile is not null && !OperatingSystem.IsBrowser())
            await SaveCurrent(automatic: true);
        else await SaveSessionSafely();
    }

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
            foreach (var tab in tabs) TrackEditor(tab.Editor);
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
        activeTab.File = currentFile; activeTab.Bookmark = currentBookmark;
        foreach (var other in snapshot.OtherTabs)
        {
            var state = new EditorState(editor.Preferences); other.Restore(state);
            IStorageFile? otherFile = null;
            if (storage is not null)
            {
                try { if (other.Bookmark is not null) otherFile = await storage.OpenFileBookmarkAsync(other.Bookmark); }
                catch (Exception) { }
                try { if (otherFile is null && other.LocalPath is not null) otherFile = await storage.TryGetFileFromPathAsync(other.LocalPath); }
                catch (Exception) { }
            }
            var tab = new ProjectTab(state)
            {
                Bookmark = other.Bookmark,
                File = otherFile is null ? null : new ProjectFile(otherFile, other.DiskHash),
                Dirty = other.Dirty
            };
            if (!other.Dirty && tab.File is not null)
                try
                {
                    var latest = await tab.File.Read(); state.Load(latest, otherFile!.Name);
                    state.SelectFrame(Math.Clamp(other.Frame, 0, state.Scene.Frames.Count - 1));
                    tab.File = new ProjectFile(otherFile!, ProjectFile.Hash(latest));
                }
                catch (Exception) { tab.File = null; state.MarkUnsaved(); tab.Dirty = true; }
            if (tab.File is null && other.FileName is not null) { state.MarkUnsaved(); tab.Dirty = true; }
            tabs.Add(tab);
        }
        if (tabs.Count > 1)
        {
            tabs.Remove(activeTab);
            tabs.Insert(Math.Clamp(snapshot.ActiveTab, 0, tabs.Count), activeTab);
        }
        SetSaveMessage(snapshot.Dirty ? "Recovered unsaved edits from your last session." : "Reopened your last cutscene.");
        Build(compact);
        try { await CutsceneFonts.EnsureAsync(editor.Scene); RefreshCanvas(); QueueFontCheck(); }
        catch (Exception ex) { SetSaveMessage("Cutscene recovered; font loading failed: " + ex.Message, true); }
        foreach (var tab in tabs.Where(tab => tab != activeTab))
            try { await CutsceneFonts.EnsureAsync(tab.Editor.Scene); }
            catch (Exception) { tab.Dirty = true; }
    }

    private async Task BindFile(IStorageFile file, byte[] bytes)
    {
        currentFile = new ProjectFile(file, ProjectFile.Hash(bytes)); currentBookmark = null;
        recoveryBlocked = false;
        try { if (file.CanBookmark) currentBookmark = await file.SaveBookmarkAsync(); }
        catch (Exception) { /* Local paths and the recovery copy remain available. */ }
        autoSavePaused = false; lastAutoSave = DateTimeOffset.UtcNow;
        activeTab.File = currentFile; activeTab.Bookmark = currentBookmark;
        activeTab.AutoSavePaused = false; activeTab.LastAutoSave = lastAutoSave;
    }

    private void ClearFile()
    {
        currentFile = null; currentBookmark = null; autoSavePaused = false;
        recoveryBlocked = false;
        lastAutoSave = DateTimeOffset.UtcNow;
        activeTab.File = null; activeTab.Bookmark = null; activeTab.AutoSavePaused = false;
        SetSaveMessage("Choose Save to give this cutscene a file. Crash recovery is active.");
    }

    private async Task SaveSession()
    {
        if (!sessionReady || !SessionStorage.Enabled || recoveryBlocked) return;
        await sessionLock.WaitAsync();
        try
        {
            var captures = tabs.Select(tab =>
            {
                var state = tab.Editor;
                return (Scene: state.Scene.Snapshot(), Saved: state.SavedProject,
                    state.FileName, Frame: state.FrameIndex, Bookmark: tab == activeTab ? currentBookmark : tab.Bookmark,
                    LocalPath: (tab == activeTab ? currentFile : tab.File)?.File.TryGetLocalPath(),
                    DiskHash: (tab == activeTab ? currentFile : tab.File)?.DiskHash);
            }).ToArray();
            var activeIndex = tabs.IndexOf(activeTab);
            var json = await Task.Run(() =>
            {
                SessionSnapshot Capture(int index)
                {
                    var capture = captures[index];
                    var bytes = CutsceneFile.Write(capture.Scene);
                    return new SessionSnapshot
                    {
                        Project = bytes,
                        FileName = capture.FileName,
                        Frame = capture.Frame,
                        Bookmark = capture.Bookmark,
                        LocalPath = capture.LocalPath,
                        DiskHash = capture.DiskHash,
                        Dirty = capture.Saved is null || !bytes.AsSpan().SequenceEqual(capture.Saved)
                    };
                }
                var snapshot = Capture(activeIndex); snapshot.ActiveTab = activeIndex;
                for (var i = 0; i < captures.Length; i++) if (i != activeIndex) snapshot.OtherTabs.Add(Capture(i));
                return snapshot.Serialize();
            });
            if (json == lastSessionJson) return;
            await SessionStorage.Write(json); lastSessionJson = json;
        }
        finally { sessionLock.Release(); }
    }

    private async Task SaveSessionSafely()
    {
        try { await SaveSession(); }
        catch (Exception ex) { SetSaveMessage("Recovery could not be saved: " + ex.Message, true); }
    }

    private Task FlushSession() => SaveSessionSafely();

    private async Task SessionTick()
    {
        await OpenPendingFiles();
        if (fileBusy || updateInstalling || dragging || pathBase is not null || cancelTouchEdit is not null) return;
        // Browser save handles may represent downloads, requiring a user gesture each time.
        if (editor.Preferences.AutoSave && !OperatingSystem.IsBrowser())
        {
            if (!autoSavePaused && currentFile is not null && DateTimeOffset.UtcNow - lastAutoSave >= TimeSpan.FromMinutes(editor.Preferences.AutoSaveMinutes))
                await SaveCurrent(automatic: true);
            foreach (var tab in tabs.Where(tab => tab != activeTab)) await AutoSaveTab(tab);
            RefreshTabs();
        }
        await SaveSessionSafely();
    }

    private async Task AutoSaveTab(ProjectTab tab)
    {
        if (tab.File is null || tab.AutoSavePaused || DateTimeOffset.UtcNow - tab.LastAutoSave < TimeSpan.FromMinutes(editor.Preferences.AutoSaveMinutes)) return;
        var state = tab.Editor; var scene = state.Scene; var snapshot = scene.Snapshot(); var revision = state.Revision;
        fileBusy = true;
        try
        {
            var bytes = await Task.Run(() => CutsceneFile.Write(snapshot));
            if (!state.DiffersFromSaved(bytes)) { tab.LastAutoSave = DateTimeOffset.UtcNow; return; }
            await tab.File.Write(bytes, checkExternalChanges: true);
            if (ReferenceEquals(state.Scene, scene)) state.MarkSaved(bytes, revision);
            tab.Dirty = state.FastDirty; tab.LastAutoSave = DateTimeOffset.UtcNow;
        }
        catch (Exception ex) { tab.AutoSavePaused = true; tab.Dirty = true; SetSaveMessage($"Autosave paused for {tab.Editor.FileName}: {ex.Message}", true); }
        finally { fileBusy = false; }
    }

    private async Task SaveCurrent(bool automatic)
    {
        if (currentFile is null || fileBusy) return;
        var file = currentFile; var state = editor; var scene = state.Scene;
        var snapshot = scene.Snapshot(); var revision = state.Revision;
        fileBusy = true;
        try
        {
            var bytes = await Task.Run(() => CutsceneFile.Write(snapshot));
            if (automatic && !state.DiffersFromSaved(bytes))
            {
                lastAutoSave = DateTimeOffset.UtcNow;
                activeTab.LastAutoSave = lastAutoSave;
                await SaveSessionSafely();
                return;
            }
            // Recovery and the project file have independent destinations.
            var recovery = SaveSessionSafely();
            try { await file.Write(bytes, checkExternalChanges: true); }
            finally { await recovery; }
            if (ReferenceEquals(editor, state) && ReferenceEquals(state.Scene, scene) && ReferenceEquals(currentFile, file)) state.MarkSaved(bytes, revision);
            lastAutoSave = DateTimeOffset.UtcNow; autoSavePaused = false;
            activeTab.LastAutoSave = lastAutoSave; activeTab.AutoSavePaused = false; activeTab.Dirty = state.FastDirty;
            SetSaveMessage($"{(automatic ? "Autosaved" : "Saved")} {file.File.Name} at {DateTime.Now:t}.");
            RefreshTitle(); // The periodic recovery tick will mark this snapshot clean.
        }
        catch (Exception ex)
        {
            autoSavePaused = true;
            SetSaveMessage("Autosave paused: " + ex.Message, true);
            if (!automatic) await ShowError(ex.Message);
        }
        finally { fileBusy = false; if (state.FastDirty && editor.Preferences.AutoSave && !autoSavePaused) QueueIdleSave(); }
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
