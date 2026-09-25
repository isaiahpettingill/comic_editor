using Avalonia.Controls;
using Avalonia.Headless;
using ComicEditor.Format;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Fact]
    public async Task InlineTextEditsSelectedLanguageAndUndoesAsOneChange()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var state = State(view);
            var window = new Window { Content = view, Width = 1200, Height = 800 }; window.Show(); _ = Capture(window);
            var obj = new TextObject { Key = "line", X = 8, Y = 8, Width = 100, Height = 30 };
            state.Frame.TextObjects.Add(obj); state.Scene.Translations["en"]["line"] = "Hello";
            state.Language = "es"; state.SelectedTextId = obj.Id;
            Invoke(view, "RefreshAll"); Invoke(view, "BeginInlineTextEdit");
            var box = Named<TextBox>(window, "InlineTranslationText");
            box.Text = "¡Hola!"; box.Text = "¡Hola, amigo!"; _ = Capture(window);
            Assert.Equal("¡Hola, amigo!", state.Scene.Text("es", "line"));
            Assert.Equal("Hello", state.Scene.Text("en", "line"));
            box.SelectionStart = 1; box.SelectionEnd = 5;
            Click(window, Named<Button>(window, "InlineFontLarger"));
            Assert.Equal((1, 4, 17d), (obj.Styles.Single().Start, obj.Styles.Single().Length, obj.Styles.Single().FontSize));
            Invoke(view, "EndInlineTextEdit");
            Assert.True(state.Undo());
            Assert.Equal("", state.Scene.Text("es", "line"));
            window.Close();
        }, CancellationToken.None);
    }
}
