using Avalonia.Controls;
using Avalonia.Headless;
using ComicEditor.Editing;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Fact]
    public async Task ProjectTabsKeepSeparateArtworkAndUndoHistory()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var window = new Window { Content = view, Width = 1200, Height = 800 };
            window.Show(); _ = Capture(window);
            var first = State(view); first.BeforeChange(); first.Layer.SetPixel(1, 1, 2);
            Invoke(view, "New"); _ = Capture(window);
            var second = State(view); Assert.NotSame(first, second);
            Assert.Equal(-1, second.Layer.Pixel(1, 1));
            second.BeforeChange(); second.Layer.SetPixel(2, 2, 3);
            Click(window, Named<Button>(window, "ProjectTab0"));
            Assert.Same(first, State(view)); Assert.Equal(2, first.Layer.Pixel(1, 1));
            Assert.True(first.Undo()); Assert.Equal(-1, first.Layer.Pixel(1, 1));
            Click(window, Named<Button>(window, "ProjectTab1"));
            Assert.Same(second, State(view)); Assert.Equal(3, second.Layer.Pixel(2, 2));
            Assert.True(second.Undo()); Assert.Equal(-1, second.Layer.Pixel(2, 2));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RecoveryRestoresMultipleTabsAndActiveTab()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(async () =>
        {
            var first = new EditorState(); first.Scene.Palette[0] = "#123456";
            var second = new EditorState(); second.Scene.Palette[0] = "#654321";
            var snapshot = SessionSnapshot.Capture(second);
            snapshot.OtherTabs.Add(SessionSnapshot.Capture(first)); snapshot.ActiveTab = 1;
            var view = new MainView(); var window = new Window { Content = view, Width = 1200, Height = 800 };
            window.Show(); _ = Capture(window);
            await (Task)Invoke(view, "RestoreSession", snapshot)!;
            Assert.Equal("#654321", State(view).Scene.Palette[0]);
            Click(window, Named<Button>(window, "ProjectTab0"));
            Assert.Equal("#123456", State(view).Scene.Palette[0]);
            window.Close();
        }, CancellationToken.None);
    }
}
