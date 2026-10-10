using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using ComicEditor.Editing;
using ComicEditor.Format;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Theory]
    [InlineData("solarized-light")]
    [InlineData("dark")]
    public async Task InlineTextKeepsPaperVisibleAndSyncsWithTranslation(string theme)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var state = State(view);
            var window = new Window { Content = view, Width = 1200, Height = 800 };
            window.Show(); _ = Capture(window);
            Invoke(view, "SetTheme", theme);
            var obj = state.CreateText(); obj.X = 8; obj.Y = 8; obj.Width = 100; obj.Height = 40;
            state.Frame.TextObjects.Add(obj); state.SelectedTextId = obj.Id; state.Tool = Tool.Text;
            Invoke(view, "RefreshAll"); Invoke(view, "BeginInlineTextEdit"); _ = Capture(window);
            var inline = Named<TextBox>(window, "InlineTranslationText");
            inline.Focus(); _ = Capture(window);
            Assert.True(inline.IsFocused);
            var border = inline.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_BorderElement");
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color);
            var translation = Named<TextBox>(window, "TranslationText");
            translation.Focus(); translation.Text = "Primary translation"; _ = Capture(window);
            Assert.Equal("Primary translation", inline.Text);
            inline.Focus(); inline.Text = "Canvas edit"; _ = Capture(window);
            Assert.Equal("Canvas edit", translation.Text);
            translation.Focus(); translation.Text = "Final translation"; _ = Capture(window);
            Invoke(view, "EndInlineTextEdit"); _ = Capture(window);
            Assert.Equal("Final translation", state.Scene.Text(state.Language, obj.Key));
            window.Close();
        }, CancellationToken.None);
    }

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
