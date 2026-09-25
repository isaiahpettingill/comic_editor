using Avalonia.Controls;
using Avalonia.Headless;
using ComicEditor.Format;
using ComicEditor.Rendering;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Fact]
    public async Task HighlightedStyleChangesRenderedTextSize()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var scene = Cutscene.Create(180, 80);
            var obj = new TextObject { Key = "line", X = 2, Y = 2, Width = 170, Height = 70, FontSize = 12 };
            scene.Frames[0].TextObjects.Add(obj); scene.Translations["en"]["line"] = "Hades";
            var small = DisplayCompiler.Rasterize(scene, obj, "Hades", "en")!;
            obj.ChangeStyle("en", "Hades", 0, 5, fontSize: 28);
            var large = DisplayCompiler.Rasterize(scene, obj, "Hades", "en")!;
            Assert.True(large.Height > small.Height || large.Width > small.Width);
            Assert.NotEmpty(DisplayCompiler.Compile(scene).Frames[0].Text[0].Runs);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task LanguagePlacementChangesCompiledPositionAndEditorKeepsFallbackLayout()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var scene = Cutscene.Create(160, 90);
            var obj = new TextObject { Key = "line", X = 2, Y = 2, Width = 60, Height = 30, FontSize = 16 };
            obj.SetPlacement("es", "en", new TextPlacement { X = 65, Y = 35, Width = 90, Height = 30 });
            scene.Frames[0].TextObjects.Add(obj);
            scene.Translations["en"]["line"] = "Hello"; scene.Translations["es"]["line"] = "Hola";
            var compiled = DisplayCompiler.Compile(scene);
            var en = compiled.Frames[0].Text[compiled.Languages.IndexOf("en")].Runs.Single();
            var es = compiled.Frames[0].Text[compiled.Languages.IndexOf("es")].Runs.Single();
            Assert.InRange(en.X, 2u, 61u); Assert.InRange(es.X, 65u, 154u);
            Assert.InRange(es.Y, 35u, 64u);

            var view = new MainView(); var state = State(view);
            var window = new Window { Content = view, Width = 1200, Height = 800 }; window.Show(); _ = Capture(window);
            var edit = new TextObject { Key = "line", X = 2, Y = 2, Width = 60, Height = 30 };
            state.Frame.TextObjects.Add(edit); state.Language = "es"; state.SelectedTextId = edit.Id;
            Invoke(view, "RefreshAll"); Invoke(view, "EditTextProperties", edit); _ = Capture(window);
            Named<NumericUpDown>(window, "TextX").Value = 70;
            Click(window, Named<Button>(window, "ModalApply"));
            Assert.Equal(2, edit.X); Assert.Equal(70, edit.Placement("es", "en").X);
            window.Close();
        }, CancellationToken.None);
    }
}
