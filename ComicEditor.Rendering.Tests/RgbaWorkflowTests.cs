using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using ComicEditor.Editing;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
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
