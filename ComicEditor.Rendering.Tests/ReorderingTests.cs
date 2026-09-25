using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using ComicEditor.Format;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Fact]
    public async Task DraggingStoryboardRowReordersFrameAndCanUndo()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView();
            var window = new Window { Content = view, Width = 1280, Height = 800 };
            window.Show();
            var editor = State(view);
            editor.AddFrame(false);
            editor.AddFrame(false);
            var order = editor.Scene.Frames.Select(frame => frame.Id).ToArray();
            Invoke(view, "RefreshAll");
            _ = Capture(window);

            var source = Named<Border>(window, "FrameRow0");
            var target = Named<Border>(window, "FrameRow2");
            var start = source.TranslatePoint(new Point(20, source.Bounds.Height / 2), window)!.Value;
            var end = target.TranslatePoint(new Point(20, target.Bounds.Height * .75), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);

            Assert.Equal([order[1], order[2], order[0]], editor.Scene.Frames.Select(frame => frame.Id));
            Assert.Equal(2, editor.FrameIndex);
            Assert.True(editor.Undo());
            Assert.Equal(order, editor.Scene.Frames.Select(frame => frame.Id));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DraggingArtworkLayerReordersItAndCanUndo()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView();
            var window = new Window { Content = view, Width = 1280, Height = 800 };
            window.Show();
            var editor = State(view);
            editor.Frame.Layers.Add(ArtworkLayer.Create("Middle", editor.Scene.Width, editor.Scene.Height));
            editor.Frame.Layers.Add(ArtworkLayer.Create("Top", editor.Scene.Width, editor.Scene.Height));
            var order = editor.Frame.Layers.Select(layer => layer.Id).ToArray();
            Invoke(view, "RefreshAll");
            _ = Capture(window);

            var source = Named<Border>(window, "LayerRow2").GetVisualDescendants().OfType<Border>().Single();
            var target = Named<Border>(window, "LayerRow0");
            var start = source.TranslatePoint(new Point(20, source.Bounds.Height / 2), window)!.Value;
            var end = target.TranslatePoint(new Point(20, target.Bounds.Height * .75), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);

            Assert.Equal([order[2], order[0], order[1]], editor.Frame.Layers.Select(layer => layer.Id));
            Assert.Equal(0, editor.LayerIndex);
            Assert.True(editor.Undo());
            Assert.Equal(order, editor.Frame.Layers.Select(layer => layer.Id));
            window.Close();
        }, CancellationToken.None);
    }
}
