using System.Reflection;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ComicEditor.Editing;
using ComicEditor.Fonts;
using ComicEditor.Format;
using ComicEditor.Rendering;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Theory]
    [InlineData("海", "en", "Noto Sans SC")]
    [InlineData("海", "zh-Hant", "Noto Sans TC")]
    [InlineData("海", "ja", "Noto Sans JP")]
    [InlineData("海", "ko", "Noto Sans KR")]
    [InlineData("あ", "en", "Noto Sans JP")]
    [InlineData("ع", "ar", "Noto Sans Arabic")]
    [InlineData("ש", "he", "Noto Sans Hebrew")]
    [InlineData("अ", "hi", "Noto Sans Devanagari")]
    [InlineData("ก", "th", "Noto Sans Thai")]
    public void ScriptAndPreviewLanguageSelectAppropriateFont(string text, string language, string family)
        => Assert.Equal(family, LanguageFonts.FamilyFor(text.EnumerateRunes().First(), language));

    [Fact]
    public async Task CoreFontsWorkOfflineAndChineseRequiresInstallation()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            Assert.Empty(CutsceneFonts.Missing("Hello ¡océano! Português 😀 🎨", "comic-shanns", "en"));
            Assert.Empty(CutsceneFonts.Missing("\n\t\u200d\ufe0f", "comic-shanns", "en"));
            var missing = Assert.Single(CutsceneFonts.Missing("海洋警告", "comic-shanns", "zh-CN"));
            Assert.Equal("Noto Sans SC", missing.Family);
            var scene = Cutscene.Create(160, 60);
            scene.Translations["en"]["line"] = "海洋警告";
            scene.Frames[0].TextObjects.Add(new TextObject { Key = "line", Width = 150, Height = 58 });
            Assert.Throws<InvalidDataException>(() => DisplayCompiler.Compile(scene));
            using var png = new MemoryStream();
            Assert.Throws<InvalidDataException>(() => PngExporter.Write(png, scene, 0, "en"));
            FontFixture.Register();
            Assert.Empty(CutsceneFonts.Missing(scene));
            Assert.NotEmpty(DisplayCompiler.Compile(scene).Frames[0].Text[0].Runs);
            scene.FallbackFontIds.Add(missing.Id);
            Assert.Equal(scene.FallbackFontIds, CutsceneFile.Parse(CutsceneFile.Write(scene)).FallbackFontIds);
        }, CancellationToken.None);
    }

    private static void Set(MainView view, string field, object value) => typeof(MainView).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, value);
    private static Task Check(MainView view, bool explicitRequest = true) => (Task)typeof(MainView).GetMethod("CheckFontsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, [CancellationToken.None, explicitRequest])!;
    private static void Close(MainView view) => typeof(MainView).GetMethod("CloseModal", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, null);

    [Fact]
    public async Task PromptRequiresConsentDeclinePreservesTextAndInstallUpdatesProject()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(async () =>
        {
            var view = new MainView();
            var editor = (EditorState)typeof(MainView).GetField("editor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
            var text = new TextObject { Key = "line", Width = 150, Height = 58 };
            editor.Frame.TextObjects.Add(text); editor.Scene.Translations["en"][text.Key] = "海洋警告";
            editor.Tool = Tool.Text; editor.SelectedTextId = text.Id;
            var downloads = 0;
            Set(view, "fontOnline", (Func<CancellationToken, Task<bool>>)(_ => Task.FromResult(true)));
            Set(view, "loadCachedLanguageFonts", (Func<Cutscene, Task>)(_ => Task.CompletedTask));
            Set(view, "downloadLanguageFont", (Func<string, CancellationToken, Task<string>>)((_, _) =>
            { downloads++; return Task.FromResult(CutsceneFonts.Register(FontFixture.Chinese())); }));
            var window = new Window { Content = view, Width = 1000, Height = 700 }; window.Show();
            await Check(view);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Install language fonts?");
            Assert.Equal(0, downloads);
            Close(view);
            Assert.Equal("海洋警告", editor.Scene.Text("en", "line"));
            Assert.Empty(editor.Scene.FallbackFontIds);
            await Check(view); // Explicit retry is available after declining.
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ModalApply")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, downloads);
            Assert.Contains("google:Noto Sans SC", editor.Scene.FallbackFontIds);
            Assert.Empty(CutsceneFonts.Missing(editor.Scene));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "ModalOverlay");
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task OfflineTypingDoesNotPromptOrDownload()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(async () =>
        {
            var view = new MainView();
            var editor = (EditorState)typeof(MainView).GetField("editor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
            editor.Frame.TextObjects.Add(new TextObject { Key = "line" });
            editor.Scene.Translations["en"]["line"] = "海";
            Set(view, "fontOnline", (Func<CancellationToken, Task<bool>>)(_ => Task.FromResult(false)));
            Set(view, "loadCachedLanguageFonts", (Func<Cutscene, Task>)(_ => Task.CompletedTask));
            Set(view, "downloadLanguageFont", (Func<string, CancellationToken, Task<string>>)((_, _) => throw new Exception("Unexpected download")));
            var window = new Window { Content = view, Width = 1000, Height = 700 }; window.Show();
            await Check(view, explicitRequest: false);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Border>(), b => b.Name == "ModalOverlay");
            Assert.Equal("海", editor.Scene.Text("en", "line"));
            window.Close();
        }, CancellationToken.None);
    }
}
