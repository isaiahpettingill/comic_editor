using Avalonia.Platform.Storage;
using ComicEditor.Editing;
using ComicEditor.Format;

namespace ComicEditor.Rendering.Tests;

public sealed class SavePerformanceTests
{
    [Fact]
    public async Task RecoveryAndSaveShareOneSerializationUntilDocumentChanges()
    {
        var state = new EditorState();
        var cache = new ProjectSnapshotCache();
        var first = cache.Get(state);
        Assert.Same(first, cache.Get(state));
        state.BeforeChange(); state.Layer.SetPixel(0, 0, 1);
        var second = cache.Get(state);
        Assert.NotSame(first, second);
        Assert.Equal(CutsceneFile.Write(state.Scene), await second.Bytes);
        Assert.NotEqual(await first.Bytes, await second.Bytes);
        state.MarkSaved(await first.Bytes, first.Revision);
        Assert.True(state.FastDirty);
    }

    [Fact]
    public async Task LiveStrokeSnapshotsDoNotReuseAnEarlierPointInSameUndoRevision()
    {
        var state = new EditorState(); var cache = new ProjectSnapshotCache();
        state.BeforeChange(); state.Layer.SetPixel(0, 0, 1);
        var first = cache.Get(state, allowReuse: false);
        state.Layer.SetPixel(1, 0, 2);
        var second = cache.Get(state, allowReuse: false);
        Assert.NotEqual(await first.Bytes, await second.Bytes);
        Assert.Equal(CutsceneFile.Write(state.Scene), await cache.Get(state).Bytes);
    }

    [Fact]
    public async Task TypingAfterSaveInOneUndoSessionInvalidatesCacheAndDirtyState()
    {
        var state = new EditorState(); var cache = new ProjectSnapshotCache();
        state.BeforeChange(); // One checkpoint for the whole focus session.
        state.SetTranslation("en", "line.1", "A");
        var first = cache.Get(state); state.MarkSaved(await first.Bytes, first.Revision);
        Assert.False(state.FastDirty);
        state.SetTranslation("en", "line.1", "AB");
        Assert.True(state.FastDirty);
        var second = cache.Get(state); Assert.NotSame(first, second);
        state.MarkSaved(await second.Bytes, second.Revision);
        Assert.Equal("AB", CutsceneFile.Parse(state.SavedProject!).Text("en", "line.1"));
        Assert.False(state.FastDirty);
        Assert.True(state.Undo()); Assert.Equal("", state.Scene.Text("en", "line.1"));
    }

    [Fact]
    public async Task SnapshotIsIsolatedFromLaterAudioAndArtworkEdits()
    {
        var state = new EditorState(); var cache = new ProjectSnapshotCache();
        var original = CutsceneFile.Write(state.Scene);
        var capture = cache.Get(state);
        state.BeforeChange(); state.Layer.SetPixel(0, 0, 1);
        Assert.Equal(original, await capture.Bytes);
        Assert.Equal(CutsceneFile.Write(state.Scene), await cache.Get(state).Bytes);
    }

    [Fact]
    public void RgbaSerializationMatchesPerPixelReferencePng()
    {
        var scene = Cutscene.Create(17, 11, rgba: true);
        for (var y = 0; y < scene.Height; y++)
            for (var x = 0; x < scene.Width; x++)
                scene.Frames[0].Layers[0].SetRgbaPixel(x, y, (uint)(x * 1234567 + y * 7654321));
        var layer = scene.Frames[0].Layers[0];
        var expected = Enumerable.Range(0, scene.Width * scene.Height)
            .Select(i => layer.RgbaPixel(i % scene.Width, i / scene.Width)).ToArray();
        var wire = ComicEditor.Wire.CutsceneDocument.Parser.ParseFrom(CutsceneFile.Write(scene));
        Assert.Equal(RgbaPng.Encode(scene.Width, scene.Height, expected), wire.Frames[0].Layers[0].RgbaPng.ToByteArray());
        Assert.Equal(layer.Rows, CutsceneFile.Parse(CutsceneFile.Write(scene)).Frames[0].Layers[0].Rows);
    }
}

public partial class CanvasTests
{
    [Fact]
    public async Task RepeatedKeyboardSavesQueueRatherThanDroppingNewerEdits()
    {
        using var session = Avalonia.Headless.HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(async () =>
        {
            var view = new ComicEditor.Views.MainView();
            var window = new Avalonia.Controls.Window { Content = view }; window.Show();
            var path = Path.Combine(Path.GetTempPath(), "comic-save-queue-" + Guid.NewGuid().ToString("N") + ".ctsc");
            try
            {
                var editor = State(view); var original = CutsceneFile.Write(editor.Scene);
                await File.WriteAllBytesAsync(path, original);
                var file = await window.StorageProvider.TryGetFileFromPathAsync(path);
                await (Task)Invoke(view, "BindFile", file!, original)!;
                var gate = (SemaphoreSlim)typeof(ComicEditor.Views.MainView).GetField("fileSaveLock", PrivateInstance)!.GetValue(view)!;
                await gate.WaitAsync();
                editor.BeforeChange(); editor.Layer.SetPixel(0, 0, 1);
                var first = (Task)Invoke(view, "Save")!;
                editor.BeforeChange(); editor.Layer.SetPixel(1, 0, 2);
                var second = (Task)Invoke(view, "Save")!;
                Assert.False(first.IsCompleted); Assert.False(second.IsCompleted);
                gate.Release(); await Task.WhenAll(first, second);
                Assert.Equal(CutsceneFile.Write(editor.Scene), await File.ReadAllBytesAsync(path));
                Assert.False(editor.FastDirty);
            }
            finally { window.Close(); if (File.Exists(path)) File.Delete(path); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SavingDuringAStrokeCannotMarkLaterPixelsInSameRevisionClean()
    {
        using var session = Avalonia.Headless.HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(async () =>
        {
            var view = new ComicEditor.Views.MainView();
            var window = new Avalonia.Controls.Window { Content = view }; window.Show();
            var path = Path.Combine(Path.GetTempPath(), "comic-save-stroke-" + Guid.NewGuid().ToString("N") + ".ctsc");
            try
            {
                var editor = State(view); var original = CutsceneFile.Write(editor.Scene);
                await File.WriteAllBytesAsync(path, original);
                var file = await window.StorageProvider.TryGetFileFromPathAsync(path);
                await (Task)Invoke(view, "BindFile", file!, original)!;
                editor.BeforeChange(); editor.Layer.SetPixel(0, 0, 1);
                var draggingField = typeof(ComicEditor.Views.MainView).GetField("dragging", PrivateInstance)!;
                draggingField.SetValue(view, true);
                var save = (Task)Invoke(view, "Save")!;
                editor.Layer.SetPixel(1, 0, 2); // Same stroke/undo revision.
                draggingField.SetValue(view, false);
                await save;
                Assert.True(editor.FastDirty);
                Assert.Equal(-1, CutsceneFile.Parse(await File.ReadAllBytesAsync(path)).Frames[0].Layers[0].Pixel(1, 0));
                await (Task)Invoke(view, "Save")!;
                Assert.False(editor.FastDirty);
                Assert.Equal(CutsceneFile.Write(editor.Scene), await File.ReadAllBytesAsync(path));
            }
            finally { window.Close(); if (File.Exists(path)) File.Delete(path); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RecoveryFailureKeepsWindowAndUnsavedEditsOpen()
    {
        using var session = Avalonia.Headless.HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(async () =>
        {
            var view = new ComicEditor.Views.MainView();
            var window = new Avalonia.Controls.Window { Content = view }; window.Show();
            var oldWrite = SessionStorage.Write;
            var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler<Avalonia.Controls.WindowClosingEventArgs> handler = (sender, args) => Invoke(view, "ClosingSession", sender!, args);
            try
            {
                SessionStorage.Enabled = true;
                typeof(ComicEditor.Views.MainView).GetField("sessionReady", PrivateInstance)!.SetValue(view, true);
                SessionStorage.Write = _ => { attempted.TrySetResult(); return Task.FromException(new IOException("Simulated full disk")); };
                State(view).BeforeChange(); State(view).Layer.SetPixel(0, 0, 1);
                window.Closing += handler;
                window.Close();
                await attempted.Task;
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { });
                Assert.True(window.IsVisible);
                Assert.True(view.IsEnabled);
                Assert.True(State(view).FastDirty);
                Assert.False((bool)typeof(ComicEditor.Views.MainView).GetField("closingAfterRecovery", PrivateInstance)!.GetValue(view)!);
            }
            finally
            {
                window.Closing -= handler;
                SessionStorage.Enabled = false; SessionStorage.Write = oldWrite; window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SlowRecoveryDoesNotKeepManualSaveBusy()
    {
        using var session = Avalonia.Headless.HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(async () =>
        {
            var view = new ComicEditor.Views.MainView();
            var window = new Avalonia.Controls.Window { Content = view }; window.Show();
            var path = Path.Combine(Path.GetTempPath(), "comic-save-recovery-" + Guid.NewGuid().ToString("N") + ".ctsc");
            var oldWrite = SessionStorage.Write;
            var releaseRecovery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                var editor = State(view); var original = CutsceneFile.Write(editor.Scene);
                await File.WriteAllBytesAsync(path, original);
                var file = await window.StorageProvider.TryGetFileFromPathAsync(path);
                await (Task)Invoke(view, "BindFile", file!, original)!;
                SessionStorage.Enabled = true;
                typeof(ComicEditor.Views.MainView).GetField("sessionReady", PrivateInstance)!.SetValue(view, true);
                SessionStorage.Write = _ => releaseRecovery.Task;
                editor.BeforeChange(); editor.Layer.SetPixel(0, 0, 1);
                await (Task)Invoke(view, "Save")!;
                Assert.False((bool)typeof(ComicEditor.Views.MainView).GetField("fileBusy", PrivateInstance)!.GetValue(view)!);
                Assert.False(editor.FastDirty);
                Assert.Equal(CutsceneFile.Write(editor.Scene), await File.ReadAllBytesAsync(path));
            }
            finally
            {
                releaseRecovery.TrySetResult();
                await (Task)Invoke(view, "SaveSessionSafely")!;
                SessionStorage.Enabled = false; SessionStorage.Write = oldWrite;
                window.Close(); if (File.Exists(path)) File.Delete(path);
            }
        }, CancellationToken.None);
    }
}
