using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using ComicEditor.Editing;
using ComicEditor.Format;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Fact]
    public async Task ToolGroupsRememberTheirLastChoiceAndExposeRightClickMenus()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var window = new Window { Content = view, Width = 1200, Height = 800 };
            window.Show(); _ = Capture(window);
            var brush = Named<Button>(window, "ToolGroupBrush");
            Assert.Equal(4, brush.ContextMenu!.Items.Count);
            var point = brush.TranslatePoint(new Point(brush.Bounds.Width / 2, brush.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Right); window.MouseUp(point, MouseButton.Right);
            Assert.True(brush.ContextMenu.IsOpen);
            brush.ContextMenu.Close();
            Assert.Equal(4, Named<Button>(window, "ToolGroupShapes").ContextMenu!.Items.Count);
            Assert.Equal(3, Named<Button>(window, "ToolGroupSelection").ContextMenu!.Items.Count);
            Invoke(view, "ChooseTool", Tool.Smooth);
            Invoke(view, "ChooseTool", Tool.Curve);
            Click(window, Named<Button>(window, "ToolGroupBrush"));
            Assert.Equal(Tool.Smooth, State(view).Tool);
            Assert.Equal(Tool.Smooth, State(view).Preferences.LastToolInGroup["Brush"]);
            Assert.Equal(Tool.Curve, State(view).Preferences.LastToolInGroup["Line"]);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task PenHoldOpensToolMenuAndReleaseLeavesItOpen()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(async () =>
        {
            var view = new MainView(); var window = new Window { Content = view, Width = 1200, Height = 800 };
            window.Show(); _ = Capture(window);
            var button = Named<Button>(window, "ToolGroupBrush");
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            using var pen = new Pointer(Pointer.GetNextFreeId(), PointerType.Pen, true);
            var down = new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed);
            button.RaiseEvent(new PointerPressedEventArgs(button, pen, window, point, 1, down, KeyModifiers.None));
            await Task.Delay(550);
            Assert.True(button.ContextMenu!.IsOpen);
            var up = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased);
            button.RaiseEvent(new PointerReleasedEventArgs(button, pen, window, point, 2, up, KeyModifiers.None, MouseButton.Left));
            await Task.Delay(30);
            Assert.True(button.ContextMenu.IsOpen);
            Assert.Equal(Tool.Pixel, State(view).Tool);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CtrlSSavesWhileTranslationTextBoxHasFocus()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(async () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "comic-shortcut-" + Guid.NewGuid().ToString("N") + ".ctsc");
            var view = new MainView(); var state = State(view);
            var window = new Window { Content = view, Width = 1200, Height = 800 }; window.Show();
            try
            {
                var obj = new TextObject { Key = "line" }; state.Frame.TextObjects.Add(obj); state.SelectedTextId = obj.Id;
                state.Scene.Translations["en"][obj.Key] = "Before";
                var original = CutsceneFile.Write(state.Scene);
                await File.WriteAllBytesAsync(path, original);
                state.MarkSaved(original);
                var file = await window.StorageProvider.TryGetFileFromPathAsync(new Uri(path)); Assert.NotNull(file);
                await (Task)Invoke(view, "BindFile", file, original)!;
                Invoke(view, "RefreshAll"); _ = Capture(window);
                var box = Named<TextBox>(window, "TranslationText"); box.Focus(); box.Text = "After"; _ = Capture(window);
                window.KeyPress(Key.S, RawInputModifiers.Control, PhysicalKey.S, null);
                window.KeyRelease(Key.S, RawInputModifiers.Control, PhysicalKey.S, null);
                for (var i = 0; i < 20 && CutsceneFile.Parse(await File.ReadAllBytesAsync(path)).Text("en", "line") != "After"; i++)
                    await Task.Delay(20);
                Assert.Equal("After", CutsceneFile.Parse(await File.ReadAllBytesAsync(path)).Text("en", "line"));
            }
            finally { window.Close(); File.Delete(path); }
        }, CancellationToken.None);
    }

    [Fact]
    public void EllipticalSelectionMasksItsCorners()
    {
        var scene = Cutscene.Create(10, 10);
        var selection = new ArtworkSelection(scene.Frames[0].Layers[0], 1, 1, 7, 7, ellipse: true);
        Assert.False(selection.Contains(1, 1));
        Assert.True(selection.Contains(4, 4));
    }
}
