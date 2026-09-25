using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ComicEditor.Editing;
using ComicEditor.Rendering;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Theory]
    [InlineData(320, 568)]
    [InlineData(400, 840)]
    [InlineData(940, 400)]
    [InlineData(720, 320)]
    public async Task TouchLayoutKeepsCanvasAndActionsInBounds(int width, int height)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(touchLayout: true);
            var window = new Window { Content = view, Width = width, Height = height };
            window.Show(); _ = Capture(window);
            foreach (var name in new[] { "UndoButton", "RedoButton", "CompactToolPicker", "CompactZoom", "CompactOnion", "CompactDraw", "CompactFrames", "CompactLayers", "CompactColors" })
            {
                var control = Named<Control>(window, name);
                Assert.True(control.IsEffectivelyVisible, name);
                Assert.True(control.Bounds.Height >= 44, name);
                AssertInside(window, control);
            }
            var viewport = Named<ScrollViewer>(window, "CanvasViewport");
            Assert.True(viewport.Bounds.Height >= height - 200, $"Canvas viewport was only {viewport.Bounds.Height} high");
            AssertInside(window, viewport);
            Assert.Empty(view.GetVisualDescendants().OfType<Avalonia.Controls.GridSplitter>());
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<WrapPanel>(), c => c.Name == "PaletteSwatches" && c.IsEffectivelyVisible);
            SaveCapture(window, width == 400 ? "COMIC_ANDROID_PORTRAIT" : width == 940 ? "COMIC_ANDROID_LANDSCAPE" : "COMIC_ANDROID_SMALL");
            Click(window, Named<Button>(window, "CompactColors"));
            Assert.True(Named<Control>(window, "PaletteSwatches").IsEffectivelyVisible);
            Assert.Equal(128, Named<WrapPanel>(window, "PaletteSwatches").Children.Count);
            AssertInside(window, Named<Button>(window, "CompactDraw"));
            Click(window, Named<Button>(window, "CompactLayers"));
            Assert.True(Named<Control>(window, "InspectorHeader").IsEffectivelyVisible);
            AssertInside(window, Named<Button>(window, "UndoButton"));
            Click(window, Named<Button>(window, "CompactDraw"));
            Assert.True(viewport.IsEffectivelyVisible);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TouchUndoRedoAndRotationPreserveArtwork()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(touchLayout: true);
            var state = (EditorState)typeof(MainView).GetField("editor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(view)!;
            var window = new Window { Content = view, Width = 400, Height = 840 };
            window.Show(); _ = Capture(window);
            var undo = Named<Button>(window, "UndoButton"); var redo = Named<Button>(window, "RedoButton");
            Assert.False(undo.IsEnabled); Assert.False(redo.IsEnabled);
            var canvas = view.GetVisualDescendants().OfType<CutsceneCanvas>().Single(c => c.Focusable);
            var point = canvas.TranslatePoint(new Point(50.5, 50.5), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); _ = Capture(window);
            Assert.Equal(0, state.Layer.Pixel(50, 50)); Assert.True(undo.IsEnabled);
            Click(window, undo); Assert.Equal(-1, state.Layer.Pixel(50, 50)); Assert.True(redo.IsEnabled);
            Click(window, redo); Assert.Equal(0, state.Layer.Pixel(50, 50)); Assert.False(redo.IsEnabled);
            window.Width = 940; window.Height = 400; _ = Capture(window);
            Assert.Equal(0, state.Layer.Pixel(50, 50));
            AssertInside(window, Named<Button>(window, "UndoButton"));
            AssertInside(window, Named<Control>(window, "CompactNavigation"));
            // Every tool remains accessible through the same picker in either orientation.
            var picker = Named<Button>(window, "CompactToolPicker"); Click(window, picker);
            var flyout = Assert.IsType<Flyout>(picker.Flyout);
            var choices = Assert.IsType<Grid>(Assert.IsType<ScrollViewer>(flyout.Content).Content);
            Assert.Equal(Enum.GetValues<Tool>().Length, choices.Children.Count);
            var text = choices.Children.OfType<Button>().Single(b => b.Name == "CompactToolText");
            text.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); _ = Capture(window);
            Assert.Equal(Tool.Text, state.Tool);
            window.Close();
        }, CancellationToken.None);
    }

    private static void AssertInside(Window window, Control control)
    {
        var start = control.TranslatePoint(default, window)!.Value;
        var end = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)!.Value;
        Assert.InRange(start.X, -1, window.Width); Assert.InRange(start.Y, -1, window.Height);
        Assert.InRange(end.X, 0, window.Width + 1); Assert.InRange(end.Y, 0, window.Height + 1);
    }
}
