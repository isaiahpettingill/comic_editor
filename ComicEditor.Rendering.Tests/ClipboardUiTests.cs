using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using ComicEditor.Editing;
using ComicEditor.Rendering;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Fact]
    public async Task ArtworkAndFrameClipboardSurviveSwitchingProjectTabs()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var window = new Window { Content = view, Width = 1280, Height = 800 };
            window.Show();
            var fields = BindingFlags.Instance | BindingFlags.NonPublic;
            var state = (EditorState)typeof(MainView).GetField("editor", fields)!.GetValue(view)!;
            state.Layer.SetPixel(1, 1, 5);
            state.Tool = Tool.Select;
            typeof(MainView).GetField("selection", fields)!.SetValue(view, new ArtworkSelection(state.Layer, 1, 1, 2, 2));
            Invoke("CopySelection", false);
            Invoke("New");
            var other = (EditorState)typeof(MainView).GetField("editor", fields)!.GetValue(view)!;
            Invoke("PasteSelection");
            Assert.Equal(5, other.Layer.Pixel(5, 5));

            Invoke("CopyFrame", 0); Invoke("New");
            var third = (EditorState)typeof(MainView).GetField("editor", fields)!.GetValue(view)!;
            Invoke("PasteFrame", 0);
            Assert.Equal(2, third.Scene.Frames.Count);
            Assert.Equal(5, third.Scene.Frames[1].Layers[0].Pixel(5, 5));
            Assert.NotEqual(third.Scene.Frames[0].Id, third.Scene.Frames[1].Id);
            third.Scene.Validate();

            var canvas = (CutsceneCanvas)typeof(MainView).GetField("canvas", fields)!.GetValue(view)!;
            Assert.Equal(3, canvas.ContextMenu!.Items.Count);
            var storyboard = (StackPanel)typeof(MainView).GetField("storyboard", fields)!.GetValue(view)!;
            var row = (Border)storyboard.Children[0];
            Assert.Equal(2, row.ContextMenu!.Items.Count);
            _ = Capture(window);
            var copiedBeforeShortcut = typeof(MainView).GetField("clipboardFrame", fields)!.GetValue(view);
            Assert.True(row.Focus()); window.KeyPress(Key.C, RawInputModifiers.Control, PhysicalKey.C, null);
            Assert.NotSame(copiedBeforeShortcut, typeof(MainView).GetField("clipboardFrame", fields)!.GetValue(view));
            Invoke("New");
            var fourth = (EditorState)typeof(MainView).GetField("editor", fields)!.GetValue(view)!;
            _ = Capture(window);
            Assert.True(((CutsceneCanvas)typeof(MainView).GetField("canvas", fields)!.GetValue(view)!).Focus());
            window.KeyPress(Key.V, RawInputModifiers.Control, PhysicalKey.V, null);
            Assert.Equal(2, fourth.Scene.Frames.Count);
            window.Close();

            void Invoke(string name, params object?[] args) =>
                typeof(MainView).GetMethod(name, fields)!.Invoke(view, args);
        }, CancellationToken.None);
    }
}
