using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Platform.Storage;
using ComicEditor.Editing;
using ComicEditor.Format;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReopeningUsesLatestCleanFileOrRecoversUnsavedEdits(bool dirty, bool missing)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(async () =>
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "comic-reopen-" + Guid.NewGuid().ToString("N") + ".cutscene");
            var prior = new EditorState(); prior.FileName = "remembered.cutscene";
            var original = CutsceneFile.Write(prior.Scene);
            if (dirty) prior.Scene.Palette[0] = "#123456";
            var snapshot = SessionSnapshot.Capture(prior); snapshot.LocalPath = path; snapshot.DiskHash = ProjectFile.Hash(original);
            var changedOnDisk = CutsceneFile.Parse(original); changedOnDisk.Palette[0] = "#654321";
            if (!missing) await File.WriteAllBytesAsync(path, CutsceneFile.Write(changedOnDisk));
            var view = new MainView(); var window = new Window { Content = view }; window.Show();
            try
            {
                await (Task)Invoke(view, "RestoreSession", snapshot)!;
                Assert.Equal(dirty ? "#123456" : "#654321", State(view).Scene.Palette[0]);
                Assert.Equal(dirty, State(view).IsDirty);
                Assert.Equal(missing, typeof(MainView).GetField("currentFile", PrivateInstance)!.GetValue(view) is null);
            }
            finally { window.Close(); if (File.Exists(path)) File.Delete(path); }
        }, CancellationToken.None);
    }
    [Theory]
    [InlineData(320, 568)]
    [InlineData(1280, 800)]
    public async Task AutosaveDialogFitsAndRemembersSettings(int width, int height)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(width < 900);
            var window = new Window { Content = view, Width = width, Height = height }; window.Show(); _ = Capture(window);
            Invoke(view, "ShowAutosave"); _ = Capture(window);
            AssertInside(window, Named<Border>(window, "ModalCard"));
            Named<CheckBox>(window, "EnableAutosave").IsChecked = true;
            Named<NumericUpDown>(window, "AutosaveInterval").Value = 3;
            SaveCapture(window, width < 900 ? "COMIC_AUTOSAVE_MOBILE" : "COMIC_AUTOSAVE_DESKTOP");
            Click(window, Named<Button>(window, "ModalApply"));
            Assert.True(State(view).Preferences.AutoSave); Assert.Equal(3, State(view).Preferences.AutoSaveMinutes);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AutosaveOnlyWritesWhenEnabledAndDueAndNewClearsTheFile()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(async () =>
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "comic-autosave-" + Guid.NewGuid().ToString("N") + ".cutscene");
            var view = new MainView(); var editor = State(view);
            var window = new Window { Content = view }; window.Show();
            try
            {
                var original = CutsceneFile.Write(editor.Scene); await File.WriteAllBytesAsync(path, original);
                var file = await window.StorageProvider.TryGetFileFromPathAsync(path); Assert.NotNull(file);
                await (Task)Invoke(view, "BindFile", file, original)!;
                editor.BeforeChange(); editor.Scene.Palette[0] = "#123456";
                typeof(MainView).GetField("lastAutoSave", PrivateInstance)!.SetValue(view, DateTimeOffset.MinValue);
                await (Task)Invoke(view, "SessionTick")!; Assert.Equal(original, await File.ReadAllBytesAsync(path));
                editor.Preferences.AutoSave = true;
                await (Task)Invoke(view, "SessionTick")!; var written = await File.ReadAllBytesAsync(path);
                Assert.Equal(CutsceneFile.Write(editor.Scene), written); Assert.False(editor.IsDirty);
                editor.BeforeChange(); editor.Scene.Palette[0] = "#654321";
                await (Task)Invoke(view, "SessionTick")!; Assert.Equal(written, await File.ReadAllBytesAsync(path));
                // An external write pauses autosave without destroying the editor's work.
                await File.WriteAllBytesAsync(path, original);
                typeof(MainView).GetField("lastAutoSave", PrivateInstance)!.SetValue(view, DateTimeOffset.MinValue);
                await (Task)Invoke(view, "SessionTick")!;
                Assert.True(editor.IsDirty); Assert.Equal(original, await File.ReadAllBytesAsync(path));
                Invoke(view, "New"); editor.Scene.Palette[0] = "#123456";
                typeof(MainView).GetField("lastAutoSave", PrivateInstance)!.SetValue(view, DateTimeOffset.MinValue);
                await (Task)Invoke(view, "SessionTick")!; Assert.Equal(original, await File.ReadAllBytesAsync(path));
            }
            finally { window.Close(); File.Delete(path); }
        }, CancellationToken.None);
    }
}

public sealed class SessionTests
{
    [Fact]
    public void RecoverySnapshotPreservesOtherOpenTabs()
    {
        var first = new EditorState(); var second = new EditorState();
        second.Scene.Palette[0] = "#123456";
        var snapshot = SessionSnapshot.Capture(first);
        snapshot.OtherTabs.Add(SessionSnapshot.Capture(second)); snapshot.ActiveTab = 1;
        var restored = SessionSnapshot.Parse(snapshot.Serialize())!;
        Assert.Equal(1, restored.ActiveTab);
        Assert.Single(restored.OtherTabs);
        Assert.True(restored.OtherTabs[0].Dirty);
        Assert.Equal("#123456", CutsceneFile.Parse(restored.OtherTabs[0].Project).Palette[0]);
    }

    [Fact]
    public void RecoveryCaptureReusesProjectBytesFromSave()
    {
        var editor = new EditorState();
        editor.Scene.Palette[0] = "#123456";
        var project = CutsceneFile.Write(editor.Scene);
        var snapshot = SessionSnapshot.Capture(editor, project);
        Assert.Same(project, snapshot.Project);
        Assert.True(snapshot.Dirty);
        editor.MarkSaved(project);
        Assert.False(SessionSnapshot.Capture(editor, project).Dirty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecoveryRoundTripsUntitledAndDirtyProjects(bool dirty)
    {
        var editor = new EditorState(); editor.AddFrame(true); editor.MarkSaved();
        if (dirty) editor.Scene.Palette[0] = "#123456";
        var snapshot = SessionSnapshot.Capture(editor);
        snapshot.Bookmark = "saved-access"; snapshot.LocalPath = "/tmp/test.cutscene"; snapshot.DiskHash = "diskhash";
        var parsed = SessionSnapshot.Parse(snapshot.Serialize())!;
        var restored = new EditorState(); parsed.Restore(restored);
        Assert.Equal(CutsceneFile.Write(editor.Scene), CutsceneFile.Write(restored.Scene));
        Assert.Equal(1, restored.FrameIndex); Assert.Equal(dirty, restored.IsDirty); Assert.Null(restored.FileName);
        Assert.Equal(snapshot.Bookmark, parsed.Bookmark); Assert.Equal(snapshot.LocalPath, parsed.LocalPath); Assert.Equal(snapshot.DiskHash, parsed.DiskHash);
    }

    [Fact]
    public void EditsMadeDuringAnAsyncSaveStayDirty()
    {
        var editor = new EditorState(); editor.Scene.Palette[0] = "#123456";
        var captured = CutsceneFile.Write(editor.Scene);
        editor.Scene.Palette[0] = "#654321"; editor.MarkSaved(captured);
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public async Task RecoveryReplacesAtomicallyAndRetainsPriorCopyOnFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "comic-session-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var path = Path.Combine(root, "last-session.json");
        try
        {
            await SessionStorage.WriteAtomic(path, "first");
            await SessionStorage.WriteAtomic(path, "second"); Assert.Equal("second", await File.ReadAllTextAsync(path));
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (OperatingSystem.IsWindows())
                {
                    var error = await Record.ExceptionAsync(() => SessionStorage.WriteAtomic(path, "third"));
                    Assert.True(error is IOException or UnauthorizedAccessException);
                }
            }
            Assert.Equal("second", await File.ReadAllTextAsync(path)); Assert.Single(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AutosavePreferenceDefaultsAndInvalidIntervalsAreSafe()
    {
        Assert.False(EditorPreferences.Parse(null).AutoSave);
        Assert.Equal(1, EditorPreferences.Parse("{\"AutoSaveMinutes\":0}").AutoSaveMinutes);
        Assert.Equal(60, EditorPreferences.Parse("{\"AutoSaveMinutes\":999}").AutoSaveMinutes);
    }
}
