using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using ComicEditor.Format;
using ComicEditor.Rendering;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Fact]
    public async Task FrameSettingsBadgesAndVariantPreviewPreserveEditorSelection()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var state = State(view);
            state.Scene.Frames.Add(Frame.Create(state.Scene.Width, state.Scene.Height));
            var window = new Window { Content = view, Width = 1200, Height = 800 }; window.Show(); Invoke(view, "RefreshAll"); _ = Capture(window);
            Assert.NotNull(Named<Button>(window, "FrameOptions1"));
            Invoke(view, "FrameSettings", 1); _ = Capture(window);
            Named<NumericUpDown>(window, "FrameDuration").Value = 80;
            Named<ComboBox>(window, "FrameShow").SelectedIndex = 2;
            Named<TextBox>(window, "FrameVariable").Text = "PLAYER_A";
            SaveCapture(window, "COMIC_FRAME_SETTINGS_SCREENSHOT");
            Click(window, Named<Button>(window, "ModalApply"));
            SaveCapture(window, "COMIC_FRAME_BADGES_SCREENSHOT");
            Assert.Equal(80, state.Scene.Frames[1].DurationMs); Assert.Equal("PLAYER_A", state.Scene.Frames[1].Requirement);
            Assert.Equal(0, state.FrameIndex);
            Assert.Equal("◷ 0.08s", Named<TextBlock>(window, "FrameTiming1").Text);
            Assert.Equal("PLAYER_A", Named<TextBlock>(window, "FrameRequirement1").Text);
            var compiled = DisplayCompiler.Compile(state.Scene);
            Assert.Equal(new[] { "PLAYER_A" }, compiled.Vars);
            Assert.Equal(80u, compiled.Frames[1].DurationMs); Assert.Equal("PLAYER_A", compiled.Frames[1].Req);
            state.Undo(); Assert.Equal(1000, state.Scene.Frames[1].DurationMs);
            state.Redo(); Assert.Equal(80, state.Scene.Frames[1].DurationMs);
            Invoke(view, "PreviewPlayback"); _ = Capture(window);
            var check = Named<CheckBox>(window, "PlaybackVar_PLAYER_A"); check.IsChecked = true;
            SaveCapture(window, "COMIC_PLAYBACK_SCREENSHOT");
            Named<Button>(window, "PlaybackPlay").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(Named<Button>(window, "PlaybackPlay").IsEnabled);
            Named<Button>(window, "PlaybackPause").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(Named<Button>(window, "PlaybackPlay").IsEnabled);
            Invoke(view, "CloseModal"); Assert.Equal(0, state.FrameIndex);
            Assert.Null(typeof(MainView).GetField("playbackTimer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(view));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public void CborV2KeepsOriginalFrameIndicesAndExportsSortedVariablesAndConditions()
    {
        var scene = Cutscene.Create(2, 2);
        scene.Frames[0].DurationMs = 80; scene.Frames[0].Requirement = "not B";
        scene.Frames.Add(Frame.Create(2, 2)); scene.Frames[1].Requirement = "A";
        var bytes = CborExporter.Compile(scene); var offset = 0;
        var root = (Dictionary<string, object>)Read(bytes, ref offset);
        Assert.Equal(2u, root["version"]); Assert.Equal(new object[] { "A", "B" }, (List<object>)root["vars"]);
        var frames = (List<object>)root["frames"]; Assert.Equal(2, frames.Count);
        var first = (Dictionary<string, object>)frames[0]; var second = (Dictionary<string, object>)frames[1];
        Assert.Equal(80u, first["duration_ms"]); Assert.Equal("not B", first["req"]);
        Assert.Equal("A", second["req"]); Assert.Equal(1000u, second["duration_ms"]);
    }
}
