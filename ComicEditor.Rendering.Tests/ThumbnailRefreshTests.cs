using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ComicEditor.Editing;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Fact]
    public async Task ShapeFillCanBeChangedFromCanvasToolbar()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView();
            var window = new Window { Content = view, Width = 1280, Height = 800 };
            window.Show();
            var fields = BindingFlags.NonPublic | BindingFlags.Instance;
            var editor = (EditorState)typeof(MainView).GetField("editor", fields)!.GetValue(view)!;
            editor.Tool = Tool.Rectangle;
            typeof(MainView).GetMethod("RefreshTools", fields)!.Invoke(view, null);
            var fill = view.GetVisualDescendants().OfType<ComboBox>().Single(control => control.Name == "ShapeFillToolbar");
            fill.SelectedItem = ComicEditor.Format.ShapeFill.Solid;
            Assert.Equal(ComicEditor.Format.ShapeFill.Solid, editor.Paint.Fill);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ArtworkEditsReuseStoryboardCardsAndQueueOneThumbnailTimer()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView();
            var window = new Window { Content = view, Width = 1280, Height = 800 };
            window.Show();
            var fields = BindingFlags.NonPublic | BindingFlags.Instance;
            var type = typeof(MainView);
            var editor = (EditorState)type.GetField("editor", fields)!.GetValue(view)!;
            var storyboard = (StackPanel)type.GetField("storyboard", fields)!.GetValue(view)!;
            var card = storyboard.Children[0];

            editor.BeforeChange(); editor.Layer.SetPixel(0, 0, 1);
            type.GetMethod("RefreshAll", fields)!.Invoke(view, null);
            var timer = (DispatcherTimer)type.GetField("thumbnailTimer", fields)!.GetValue(view)!;
            Assert.Same(card, storyboard.Children[0]);
            Assert.True(timer.IsEnabled);

            editor.BeforeChange(); editor.Layer.SetPixel(1, 0, 1);
            type.GetMethod("RefreshAll", fields)!.Invoke(view, null);
            Assert.Same(card, storyboard.Children[0]);
            Assert.Same(timer, type.GetField("thumbnailTimer", fields)!.GetValue(view));
            window.Close();
        }, CancellationToken.None);
    }
}
