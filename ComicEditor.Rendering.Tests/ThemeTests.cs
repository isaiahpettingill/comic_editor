using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using ComicEditor.Editing;
using ComicEditor.Format;
using ComicEditor.Rendering;
using ComicEditor.Styles;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Theory]
    [InlineData("solarized-light", false)]
    [InlineData("solarized-dark", false)]
    [InlineData("catppuccin-mocha", false)]
    [InlineData("catppuccin-latte", false)]
    [InlineData("dark", false)]
    [InlineData("gruvbox", false)]
    [InlineData("catppuccin-mocha", true)]
    [InlineData("catppuccin-latte", true)]
    public async Task ThemePersistsWithoutChangingCutsceneOrExport(string id, bool touch)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            string? saved = null;
            PreferencesStorage.Read = () => saved;
            PreferencesStorage.Write = json => saved = json;
            var view = new MainView(touch); var window = new Window { Content = view, Width = touch ? 360 : 1280, Height = touch ? 740 : 800 };
            window.Show(); _ = Capture(window);
            var editor = State(view);
            editor.Layer.SetPixel(0, 0, 0);
            var project = CutsceneFile.Write(editor.Scene);
            using var before = new MemoryStream(); PngExporter.Write(before, editor.Scene, 0, "en");
            Invoke(view, "SetTheme", id); _ = Capture(window);
            var paper = (Control)typeof(MainView).GetField("canvas", PrivateInstance)!.GetValue(view)!;
            var center = paper.TranslatePoint(new Avalonia.Point(paper.Bounds.Width / 2, paper.Bounds.Height / 2), window)!.Value;
            using (var screenshot = SkiaSharp.SKBitmap.Decode(Capture(window)))
                Assert.Equal(SkiaSharp.SKColors.White, screenshot.GetPixel((int)center.X, (int)center.Y));
            if (!touch && id != EditorThemes.Default)
                Assert.Equal(Color.Parse(EditorThemes.Find(id).Text), Assert.IsAssignableFrom<ISolidColorBrush>(Named<Button>(window, "ToolOptions").Foreground).Color);
            Assert.Equal(id, EditorPreferences.Parse(saved).Theme);
            Assert.Equal(EditorThemes.Find(id).Dark ? ThemeVariant.Dark : ThemeVariant.Light, view.ActualThemeVariant);
            Assert.Equal(Color.Parse(EditorThemes.Find(id).Text), ((SolidColorBrush)view.Foreground!).Color);
            Assert.Equal(project, CutsceneFile.Write(editor.Scene));
            using var after = new MemoryStream(); PngExporter.Write(after, editor.Scene, 0, "en");
            Assert.Equal(before.ToArray(), after.ToArray());
            if (Environment.GetEnvironmentVariable("COMIC_THEME_SCREENSHOTS") is { } folder)
            {
                Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder, id + (touch ? "-mobile" : "") + ".png"), Capture(window));
                Invoke(view, "EditPalette"); File.WriteAllBytes(Path.Combine(folder, id + (touch ? "-mobile" : "") + "-palette.png"), Capture(window));
                Invoke(view, "CloseModal");
            }
            window.Close();
            var reopened = new MainView(touch); var next = new Window { Content = reopened, Width = 1000, Height = 800 }; next.Show(); _ = Capture(next);
            Assert.Equal(id, State(reopened).Preferences.Theme);
            Assert.Equal(view.ActualThemeVariant, reopened.ActualThemeVariant);
            next.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public void UnknownThemeFallsBackToOriginalLightAppearance()
    {
        Assert.Equal(EditorThemes.Default, EditorPreferences.Parse("{\"Theme\":\"invalid\"}").Theme);
        Assert.Equal(EditorThemes.Default, EditorPreferences.Parse("{\"Theme\":null}").Theme);
        Assert.Equal(EditorThemes.Default, EditorPreferences.Parse("{}").Theme);
    }
}
