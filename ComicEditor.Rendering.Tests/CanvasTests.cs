using Avalonia;

using Avalonia.Controls;

using Avalonia.Headless;

using Avalonia.Input;

using Avalonia.Media.Imaging;

using Avalonia.Skia;

using Avalonia.VisualTree;

using ComicEditor.Editing;

using Avalonia.Themes.Fluent;

using ComicEditor.Format;

using ComicEditor.Rendering;

using ComicEditor.Views;



namespace ComicEditor.Rendering.Tests;



public sealed class TestApp : Application

{

    public override void Initialize()

    {
        PreferencesStorage.Read = () => null;
        SessionStorage.Enabled = false;
        PaletteLibrary.List = () => Task.FromResult(Array.Empty<string>());
        PreferencesStorage.Write = _ => { };

        Styles.Add(new FluentTheme());

        Styles.Add(new ComicEditor.Styles.MaterialIcons());

    }



    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()

        .UseSkia()

        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

}



public partial class CanvasTests

{

    [Fact]

    public async Task EditorLayoutRendersAtDesktopSize()

    {

        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));

        await session.Dispatch(() =>

        {

            var window = new Window { Content = new MainView(), Width = 1280, Height = 800 };

            window.Show();

            var png = Capture(window);

            Assert.True(png.Length > 1000);

            var screenshot = Environment.GetEnvironmentVariable("COMIC_EDITOR_SCREENSHOT");

            if (!string.IsNullOrWhiteSpace(screenshot)) File.WriteAllBytes(screenshot, png);

            window.Close();

        }, CancellationToken.None);

    }



    [Fact]

    public async Task EditorLayoutRendersAtNarrowSize()

    {

        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));

        await session.Dispatch(() =>

        {

            var window = new Window { Content = new MainView(), Width = 400, Height = 800 };

            window.Show();

            var png = Capture(window);

            Assert.True(png.Length > 1000);

            var screenshot = Environment.GetEnvironmentVariable("COMIC_EDITOR_COMPACT_SCREENSHOT");

            if (!string.IsNullOrWhiteSpace(screenshot)) File.WriteAllBytes(screenshot, png);

            window.Close();

        }, CancellationToken.None);

    }



    [Fact]

    public async Task DraggingPaneHeaderReordersDesktopPanes()

    {

        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));

        await session.Dispatch(() =>

        {

            var view = new MainView();

            var window = new Window { Content = view, Width = 1280, Height = 800 };

            window.Show();

            _ = Capture(window);

            var header = view.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "StoryboardHeader");

            var start = header.TranslatePoint(new Point(30, 12), window)!.Value;

            window.MouseDown(start, MouseButton.Left);

            window.MouseMove(new Point(500, start.Y), RawInputModifiers.LeftMouseButton);

            window.MouseUp(new Point(500, start.Y), MouseButton.Left);

            var order = (Array)typeof(MainView).GetField("paneOrder", System.Reflection.BindingFlags.NonPublic |

                System.Reflection.BindingFlags.Instance)!.GetValue(view)!;

            Assert.Equal("Canvas", order.GetValue(0)!.ToString());

            window.Close();

        }, CancellationToken.None);

    }



    [Fact]

    public async Task DraggingDividerResizesDesktopPanes()

    {

        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));

        await session.Dispatch(() =>

        {

            var view = new MainView();

            var window = new Window { Content = view, Width = 1280, Height = 800 };

            window.Show();

            _ = Capture(window);

            var workspace = (Grid)typeof(MainView).GetField("desktopWorkspace", System.Reflection.BindingFlags.NonPublic |

                System.Reflection.BindingFlags.Instance)!.GetValue(view)!;

            var before = workspace.ColumnDefinitions[0].ActualWidth;

            window.MouseDown(new Point(before + 2, 300), MouseButton.Left);

            window.MouseMove(new Point(before + 52, 300), RawInputModifiers.LeftMouseButton);

            window.MouseUp(new Point(before + 52, 300), MouseButton.Left);

            Assert.True(workspace.ColumnDefinitions[0].ActualWidth > before + 20);

            window.Close();

        }, CancellationToken.None);

    }



    [Fact]

    public async Task DraggingPaletteDividerResizesPalette()

    {

        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));

        await session.Dispatch(() =>

        {

            var view = new MainView();

            var window = new Window { Content = view, Width = 1280, Height = 800 };

            window.Show();

            _ = Capture(window);

            var root = (Grid)typeof(MainView).GetField("rootGrid", System.Reflection.BindingFlags.NonPublic |

                System.Reflection.BindingFlags.Instance)!.GetValue(view)!;

            var before = root.RowDefinitions[4].ActualHeight;

            var dividerY = root.RowDefinitions.Take(3).Sum(row => row.ActualHeight) + 2;

            window.MouseDown(new Point(600, dividerY), MouseButton.Left);

            window.MouseMove(new Point(600, dividerY - 40), RawInputModifiers.LeftMouseButton);

            window.MouseUp(new Point(600, dividerY - 40), MouseButton.Left);

            Assert.True(root.RowDefinitions[4].ActualHeight > before + 20);

            window.Close();

        }, CancellationToken.None);

    }



    [Fact]

    public async Task SwitchingLanguageChangesRenderedPixels()

    {

        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));

        await session.Dispatch(() =>

        {

            var scene = Cutscene.Create(96, 32);

            scene.Frames[0].TextObjects.Add(new TextObject { Key = "line.one", X = 2, Y = 2, Width = 90, Height = 28, FontSize = 14 });

            scene.Translations["en"]["line.one"] = "Hello";

            scene.Translations["es"]["line.one"] = "Adiós";

            var view = new CutsceneCanvas { Scene = scene, Width = 96, Height = 32, Language = "en" };

            var window = new Window { Content = view, Width = 96, Height = 32 };

            window.Show();

            var english = Capture(window);

            view.Language = "es"; view.InvalidateVisual();

            var spanish = Capture(window);

            Assert.NotEqual(Convert.ToHexString(english), Convert.ToHexString(spanish));

            window.Close();

        }, CancellationToken.None);

    }





    [Fact]

    public async Task ToolbarAndPaletteStayInsideTheirBounds()

    {

        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));

        await session.Dispatch(() =>

        {

            var window = new Window { Content = new MainView(), Width = 1280, Height = 800 };

            window.Show(); _ = Capture(window);

            var language = Named<ComboBox>(window, "PreviewLanguage");

            var opacity = Named<Slider>(window, "OnionOpacity");

            var thumb = opacity.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Thumb>().Single();

            var languageCenter = language.TranslatePoint(new Point(0, language.Bounds.Height / 2), window)!.Value.Y;

            var thumbCenter = thumb.TranslatePoint(new Point(0, thumb.Bounds.Height / 2), window)!.Value.Y;

            Assert.InRange(Math.Abs(languageCenter - thumbCenter), 0, 1);

            for (var i = 0; i < 128; i++)

            {

                var swatch = Named<Button>(window, "Swatch" + i);

                var bottom = swatch.TranslatePoint(new Point(swatch.Bounds.Width, swatch.Bounds.Height), window)!.Value;

                Assert.InRange(bottom.X, 1, 1280); Assert.InRange(bottom.Y, 1, 800);

            }

            var tools = Named<StackPanel>(window, "ToolRail").Children;

            Assert.Equal(11, tools.Count);

            Assert.Single(tools.Select(c => c.Bounds.X).Distinct());

            window.Close();

        }, CancellationToken.None);

    }



    [Fact]

    public async Task TextModalAndDynamicLanguagesSupportCancelApplyAndUndo()

    {

        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));

        await session.Dispatch(() =>

        {

            var view = new MainView();

            var state = (EditorState)typeof(MainView).GetField("editor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(view)!;

            var obj = new TextObject { Key = "poseidon.ocean_warning" };

            state.Frame.TextObjects.Add(obj); state.SelectedTextId = obj.Id;

            state.Scene.Translations["en"][obj.Key] = "Hades is drinking the ocean!";

            foreach (var code in new[] { "fr", "de", "ja", "it", "ko", "zh-Hant", "nl", "sv", "pl", "uk" }) state.Scene.Translations[code] = new();

            typeof(MainView).GetMethod("RefreshAll", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(view, null);

            var window = new Window { Content = view, Width = 1280, Height = 800 }; window.Show(); _ = Capture(window);

            var translation = Named<TextBox>(window, "TranslationText");

            Assert.Single(view.GetVisualDescendants().OfType<TextBox>(), t => t.AcceptsReturn);

            Named<ComboBox>(window, "TranslationLanguage").SelectedItem = "fr";

            _ = Capture(window);

            translation = Named<TextBox>(window, "TranslationText"); translation.Focus(); translation.Text = "Bonjour ocean"; _ = Capture(window);

            Assert.Equal("Bonjour ocean", state.Scene.Text("fr", obj.Key));

            Click(window, Named<Button>(window, "EditTextProperties"));

            Named<NumericUpDown>(window, "TextX").Value = 42;

            Click(window, Caption(window, "Cancel")); Assert.Equal(16, obj.X);

            Click(window, Named<Button>(window, "EditTextProperties"));

            Named<NumericUpDown>(window, "TextX").Value = 42;

            SaveCapture(window, "COMIC_EDITOR_MODAL_SCREENSHOT");

            Click(window, Named<Button>(window, "ModalApply")); Assert.Equal(16, obj.X); Assert.Equal(42, obj.Placement("fr", "en").X);

            SaveCapture(window, "COMIC_EDITOR_TEXT_SCREENSHOT");

            Click(window, Caption(window, "Languages…"));

            Named<TextBox>(window, "LanguageCode").Text = "pt-BR";

            Click(window, Caption(window, "Add")); Click(window, Named<Button>(window, "ModalApply"));

            Assert.True(state.Scene.Translations.ContainsKey("pt-BR"));

            Assert.Contains("pt-BR", Named<ComboBox>(window, "PreviewLanguage").Items.Cast<string>());

            Assert.True(state.Undo()); Assert.False(state.Scene.Translations.ContainsKey("pt-BR"));

            Assert.True(state.Undo()); Assert.Equal(16, state.Frame.TextObjects[0].Placement("fr", "en").X);

            window.Close();

        }, CancellationToken.None);

    }



    [Fact]

    public async Task MissingTranslationNeverRendersTheLocalizationKey()

    {

        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));

        await session.Dispatch(() =>

        {

            var scene = Cutscene.Create(200, 70);

            var view = new CutsceneCanvas { Scene = scene, Width = 200, Height = 70 };

            var window = new Window { Content = view, Width = 200, Height = 70 }; window.Show();

            var blank = Capture(window);

            scene.Frames[0].TextObjects.Add(new TextObject { Key = "internal.secret_key" }); view.InvalidateVisual();

            Assert.Equal(blank, Capture(window));

            view.ShowTextBounds = true; view.InvalidateVisual(); Assert.NotEqual(blank, Capture(window));

            window.Close();

        }, CancellationToken.None);

    }




    [Fact]
    public async Task TextRequiresDragDeleteRespectsTypingAndControlWheelZooms()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView();
            var state = (EditorState)typeof(MainView).GetField("editor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(view)!;
            state.Tool = Tool.Text;
            var window = new Window { Content = view, Width = 1280, Height = 800 }; window.Show(); _ = Capture(window);
            var canvas = view.GetVisualDescendants().OfType<CutsceneCanvas>().Single(c => c.Focusable);
            var start = canvas.TranslatePoint(new Point(20, 20), window)!.Value;
            var end = canvas.TranslatePoint(new Point(150, 70), window)!.Value;
            window.MouseDown(start, MouseButton.Left); window.MouseUp(start, MouseButton.Left); _ = Capture(window);
            Assert.Empty(state.Frame.TextObjects);
            window.MouseDown(start, MouseButton.Left); window.MouseMove(end, RawInputModifiers.LeftMouseButton); window.MouseUp(end, MouseButton.Left); _ = Capture(window);
            Assert.Single(state.Frame.TextObjects); Assert.InRange(state.Frame.TextObjects[0].Width, 128, 132);
            var translation = Named<TextBox>(window, "TranslationText"); translation.Focus(); window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null); window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            Assert.Single(state.Frame.TextObjects);
            canvas.Focus(); window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null); window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null); _ = Capture(window);
            Assert.Empty(state.Frame.TextObjects); Assert.True(state.Undo()); Assert.Single(state.Frame.TextObjects);
            window.MouseWheel(start, new Vector(0, 1), RawInputModifiers.Control); _ = Capture(window);
            var zoom = state.Preferences.Zoom;
            Assert.True(zoom > 0);
            window.MouseWheel(start, new Vector(0, -1), RawInputModifiers.Control); _ = Capture(window);
            var smaller = state.Preferences.Zoom;
            Assert.True(smaller < zoom); window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FractionalZoomHasNoWhiteSeams()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var scene = Cutscene.Create(10, 10); scene.Palette[0] = "#000000";
            Raster.Fill(scene.Frames[0].Layers[0], 0, 0, 0);
            var window = new Window { Content = new CutsceneCanvas { Scene = scene }, Width = 43, Height = 43 }; window.Show();
            using var bitmap = window.CaptureRenderedFrame()!;
            var size = bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;
            var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
            try
            {
                bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), buffer, size, bitmap.PixelSize.Width * 4);
                var pixels = new byte[size]; System.Runtime.InteropServices.Marshal.Copy(buffer, pixels, 0, size);
                for (var i = 0; i < size; i += 4) { Assert.Equal(0, pixels[i]); Assert.Equal(0, pixels[i + 1]); Assert.Equal(0, pixels[i + 2]); }
            }
            finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer); }
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CompilerFlattensVisibleLayersAndRasterizesChineseWithoutFontReferences()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            FontFixture.Register();
            var scene = Cutscene.Create(160, 60);
            scene.Translations["zh-CN"] = new() { ["warning"] = "海洋警告" };
            scene.Translations["en"]["warning"] = "Ocean warning";
            scene.Frames[0].Layers[0].SetPixel(0, 0, 127);
            var hidden = ArtworkLayer.Create("Not exported", 160, 60); hidden.Visible = false; hidden.SetPixel(0, 0, 4); scene.Frames[0].Layers.Add(hidden);
            scene.Frames[0].TextObjects.Add(new TextObject { Key = "warning", FontId = "google:Anton", X = 2, Y = 2, Width = 156, Height = 56, FontSize = 20 });
            Assert.True(Avalonia.Media.FontManager.Current.TryMatchCharacter('海', Avalonia.Media.FontStyle.Normal,
                Avalonia.Media.FontWeight.Normal, Avalonia.Media.FontStretch.Normal, CutsceneFonts.Resolve("google:Anton"),
                CutsceneCanvas.Culture("zh-CN"), out var fallback));
            Assert.Contains("Noto", fallback.GlyphTypeface.FamilyName);
            var result = DisplayCompiler.Compile(scene);
            Assert.Equal(127, result.Frames[0].IndexedArtwork[0]); Assert.Equal(255, result.Frames[0].IndexedArtwork[1]);
            var chinese = result.Frames[0].Text[result.Languages.IndexOf("zh-CN")];
            var english = result.Frames[0].Text[result.Languages.IndexOf("en")];
            var raster = Assert.Single(chinese.Runs);
            Assert.Equal(raster.Width * raster.Height, (uint)raster.Alpha.Length);
            Assert.Contains(raster.Alpha, value => value != 0);
            Assert.NotEqual(english.Runs[0].Alpha, raster.Alpha);
            var bytes = Google.Protobuf.MessageExtensions.ToByteArray(result);
            Assert.Equal(bytes, Google.Protobuf.MessageExtensions.ToByteArray(DisplayCompiler.Compile(scene)));
            Assert.DoesNotContain("google:Anton", System.Text.Encoding.UTF8.GetString(bytes));
            var fixture = Environment.GetEnvironmentVariable("COMIC_EDITOR_CLI_FIXTURE");
            if (!string.IsNullOrWhiteSpace(fixture))
            {
                File.WriteAllBytes(fixture, CutsceneFile.Write(scene));
                scene.Frames[0].TextObjects[0].FontId = "google:Lobster";
                File.WriteAllBytes(fixture + ".google", CutsceneFile.Write(scene));
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData("https://fonts.google.com/specimen/Noto+Sans+SC", "Noto Sans SC")]
    [InlineData("https://fonts.googleapis.com/css2?family=Permanent+Marker&display=swap", "Permanent Marker")]
    public void GoogleFontLinksPreserveStableFamilyNames(string link, string family)
    { Assert.Equal(family, ComicEditor.Fonts.GoogleFontDownload.FamilyFromLink(link)); }

    private static T Named<T>(Control root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static Button Caption(Control root, string text) => root.GetVisualDescendants().OfType<Button>().Single(b => b.Content is string s && s == text);

    private static void Click(Window window, Control control)

    {

        _ = Capture(window);

        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); _ = Capture(window);

    }

    private static void SaveCapture(Window window, string variable)

    {

        var png = Capture(window); var path = Environment.GetEnvironmentVariable(variable);

        if (!string.IsNullOrWhiteSpace(path)) File.WriteAllBytes(path, png);

    }



    private static byte[] Capture(Window window)

    {

        using var bitmap = window.CaptureRenderedFrame()!;

        using var stream = new MemoryStream();

        bitmap.Save(stream, PngBitmapEncoderOptions.Default);

        return stream.ToArray();

    }

}
