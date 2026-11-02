using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using ComicEditor.Editing;
using ComicEditor.Format;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ColorModeControlConfirmsConversionAndPreservesDocumentThroughUndoAndSave(bool compact)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(compact); var state = State(view);
            var scene = Cutscene.Create(3, 2);
            scene.Palette = ["#112233", "#112233", "#FFFFFF"];
            scene.Frames[0].Layers[0].SetPixel(0, 0, 1);
            scene.Frames[0].Layers[0].SetPixel(1, 0, 2);
            scene.Frames[0].DurationMs = 4321;
            scene.Frames[0].Requirement = "flag";
            scene.Frames[0].AttachAudio(QoaAudio.Encode(new short[80], 1, 8000));
            scene.Translations["en"]["hello"] = "Hello";
            scene.Frames[0].TextObjects.Add(new TextObject { Key = "hello", Color = 1 });
            scene.Frames.Add(scene.Frames[0].Snapshot());
            scene.Frames[1].Id = Guid.NewGuid().ToString();
            state.Load(CutsceneFile.Write(scene), "mode-test.ctsc");
            var original = CutsceneFile.Write(state.Scene);
            var window = new Window { Content = view, Width = compact ? 400 : 1200, Height = 800 };
            window.Show(); Invoke(view, "RefreshAll"); _ = Capture(window);
            if (compact) Invoke(view, "ShowCompactPage", Enum.Parse(typeof(MainView).GetNestedType("CompactPage", System.Reflection.BindingFlags.NonPublic)!, "Colors"));
            _ = Capture(window);
            var mode = Named<Button>(window, "ColorModeButton");
            Assert.Equal("Color mode: Indexed…", mode.Content);
            Click(window, mode); _ = Capture(window);
            Assert.Equal("Convert to RGBA", Named<Button>(window, "ModalApply").Content);
            Click(window, Caption(window, "Cancel"));
            Assert.False(state.Scene.IsRgba); Assert.False(state.FastDirty);
            Click(window, Named<Button>(window, "ColorModeButton"));
            Click(window, Named<Button>(window, "ModalApply")); _ = Capture(window);
            Assert.True(state.Scene.IsRgba); Assert.True(state.FastDirty);
            Assert.Equal("Color mode: RGBA…", Named<Button>(window, "ColorModeButton").Content);
            Assert.Equal(new[] { "#112233FF", "#112233FF", "#FFFFFFFF" }, state.Scene.Palette);
            foreach (var frame in state.Scene.Frames)
            {
                Assert.Equal(0x112233FFu, frame.Layers[0].RgbaPixel(0, 0));
                Assert.Equal(0xFFFFFFFFu, frame.Layers[0].RgbaPixel(1, 0));
                Assert.Equal(0u, frame.Layers[0].RgbaPixel(2, 0));
                Assert.Equal(4321, frame.DurationMs); Assert.Equal("flag", frame.Requirement);
                Assert.Equal(scene.Frames[0].AudioQoa, frame.AudioQoa);
            }
            Assert.Equal("Hello", state.Scene.Text("en", "hello"));
            var converted = CutsceneFile.Write(state.Scene);
            Invoke(view, "Undo"); _ = Capture(window);
            Assert.Equal(original, CutsceneFile.Write(state.Scene)); Assert.False(state.FastDirty);
            Assert.Equal("Color mode: Indexed…", Named<Button>(window, "ColorModeButton").Content);
            Invoke(view, "Redo"); _ = Capture(window);
            Assert.Equal(converted, CutsceneFile.Write(state.Scene)); Assert.True(state.FastDirty);
            state.MarkSaved(converted); state.Load(converted, "mode-test.ctsc");
            Invoke(view, "RefreshAll"); _ = Capture(window);
            Assert.True(state.Scene.IsRgba); Assert.False(state.FastDirty);
            Click(window, Named<Button>(window, "ColorModeButton")); _ = Capture(window);
            Assert.DoesNotContain(view.GetVisualDescendants(), c => c is Button { Name: "ModalApply" });
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("Automatic conversion to indexed color is not available") == true);
            Click(window, Caption(window, "Close")); window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RgbaModeExposesAlphaAndIndexedModeCanBeCreatedAgain()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var state = State(view);
            var window = new Window { Content = view, Width = 1200, Height = 800 }; window.Show(); _ = Capture(window);
            Invoke(view, "EnableRgbaMode");
            Assert.True(state.Scene.IsRgba);
            Assert.All(state.Scene.Palette, color => Assert.Equal(9, color.Length));
            Invoke(view, "ChooseTool", Tool.Marker);
            Assert.Equal(12, state.BrushSize);
            Assert.Equal(96, state.Paint.Opacity);
            Invoke(view, "EditPalette"); _ = Capture(window);
            Assert.True(Named<Canvas>(window, "AlphaCanvas").IsVisible);
            Named<TextBox>(window, "PaletteEditorHex").Text = "#11223380";
            Click(window, Named<Button>(window, "ModalApply"));
            Assert.Equal("#11223380", state.Scene.Palette[0]);
            Invoke(view, "NewWithMode", false);
            var next = State(view);
            Assert.NotSame(state, next);
            Assert.True(state.Scene.IsRgba);
            Assert.False(next.Scene.IsRgba);
            Assert.All(next.Scene.Palette, color => Assert.Equal(7, color.Length));
            window.Close();
        }, CancellationToken.None);
    }
}
