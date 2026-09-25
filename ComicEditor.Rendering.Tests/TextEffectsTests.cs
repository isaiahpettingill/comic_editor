using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using ComicEditor.Format;
using ComicEditor.Rendering;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Fact]
    public async Task TextEffectsSurviveEditableFileAndAlterCompiledGlyphs()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var scene = Cutscene.Create(240, 140);
            var text = new TextObject { Key = "line", X = 45, Y = 40, Width = 140, Height = 40, FontSize = 25 };
            scene.Frames[0].TextObjects.Add(text); scene.Translations["en"]["line"] = "Curved words";
            var normal = DisplayCompiler.Rasterize(scene, text, "Curved words", "en")!;
            text.RotationDegrees = 23; text.SizeEffect = TextSizeEffect.GrowThenShrink; text.CurveDegrees = -90;
            text.CurveAnchorX = .2; text.CurveAnchorY = -.1;
            var restored = CutsceneFile.Parse(CutsceneFile.Write(scene));
            var copy = restored.Frames[0].TextObjects.Single();
            Assert.Equal(23, copy.RotationDegrees); Assert.Equal(TextSizeEffect.GrowThenShrink, copy.SizeEffect);
            Assert.Equal(-90, copy.CurveDegrees);
            Assert.Equal(.2, copy.CurveAnchorX, 5); Assert.Equal(-.1, copy.CurveAnchorY, 5);
            var affected = DisplayCompiler.Rasterize(restored, copy, "Curved words", "en")!;
            Assert.True(affected.X != normal.X || affected.Y != normal.Y || affected.Width != normal.Width || affected.Height != normal.Height);
            Assert.NotEmpty(DisplayCompiler.Compile(restored).Frames[0].Text[0].Runs);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InlineTextKeepsArtworkVisibleAndTextColorUsesSwatches()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var state = State(view);
            var window = new Window { Content = view, Width = 1200, Height = 800 }; window.Show(); _ = Capture(window);
            var text = new TextObject { Key = "line", X = 8, Y = 8, Width = 100, Height = 35 };
            state.Frame.TextObjects.Add(text); state.Scene.Translations["en"]["line"] = "Hello";
            state.SelectedTextId = text.Id; Invoke(view, "RefreshAll"); _ = Capture(window);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Edit on canvas");
            Invoke(view, "BeginInlineTextEdit");
            Assert.Equal(Brushes.Transparent, Named<TextBox>(window, "InlineTranslationText").Background);
            Invoke(view, "EndInlineTextEdit");
            Invoke(view, "EditTextProperties", text); _ = Capture(window);
            var picker = Named<Button>(window, "TextColorPicker");
            var flyout = Assert.IsType<Flyout>(picker.Flyout);
            var swatches = Assert.IsType<WrapPanel>(Assert.IsType<ScrollViewer>(flyout.Content).Content);
            Assert.Equal(state.Scene.Palette.Count, swatches.Children.Count);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<ComboBox>(), c => c.ItemsSource is string[] items && items.Any(i => i.StartsWith("000  #")));
            Click(window, picker); _ = Capture(window);
            Assert.True(flyout.IsOpen);
            Click(window, Assert.IsType<Button>(swatches.Children[1]));
            Click(window, Named<Button>(window, "ModalApply"));
            Assert.Equal(1, text.Color);
            window.Close();
        }, CancellationToken.None);
    }
}
