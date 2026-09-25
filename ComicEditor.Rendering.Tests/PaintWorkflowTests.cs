using System.Buffers.Binary;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using ComicEditor.Editing;
using ComicEditor.Format;
using ComicEditor.Rendering;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static EditorState State(MainView view) => (EditorState)typeof(MainView).GetField("editor", PrivateInstance)!.GetValue(view)!;
    private static object? Invoke(MainView view, string method, params object[] args) => typeof(MainView).GetMethod(method, PrivateInstance)!.Invoke(view, args);

    [Fact]
    public void RememberedDefaultsRoundTripWithoutChangingExistingObjects()
    {
        string? saved = null;
        var preferences = new EditorPreferences { Persist = json => saved = json };
        var state = new EditorState(preferences);
        var existing = state.CreateText();
        preferences.Remember(new TextObject { FontId = "google:Anton", FontSize = 27, Bold = true, Italic = true });
        state.Scene.ResizeCanvas(480, 240, false); state.RememberCanvas();
        state.Tool = Tool.Spray; state.BrushSize = 23; state.Paint.SprayDensity = 35;
        state.Tool = Tool.Smooth; state.BrushSize = 9; state.Paint.Tip = BrushTip.Slash;
        state.Color = 111; state.Preferences.SmoothMouse = true;
        state.Scene.Palette[111] = "#123456"; state.RememberPalette();
        var reloaded = new EditorState(EditorPreferences.Parse(saved));
        Assert.Equal((480, 240), (reloaded.Scene.Width, reloaded.Scene.Height));
        var text = reloaded.CreateText(); Assert.Equal("google:Anton", text.FontId); Assert.Equal(27, text.FontSize); Assert.True(text.Bold); Assert.True(text.Italic);
        Assert.Equal("comic-shanns", existing.FontId); Assert.Equal(16, existing.FontSize);
        Assert.Equal(Tool.Smooth, reloaded.Tool); Assert.Equal(BrushTip.Slash, reloaded.Paint.Tip); Assert.Equal(9, reloaded.BrushSize);
        reloaded.Tool = Tool.Spray; Assert.Equal(23, reloaded.BrushSize); Assert.Equal(35, reloaded.Paint.SprayDensity);
        Assert.Equal(111, reloaded.Color); Assert.Equal("#123456", reloaded.Scene.Palette[111]); Assert.True(reloaded.Preferences.SmoothMouse);
        reloaded.New(); Assert.Equal(480, reloaded.Scene.Width); Assert.Equal("#123456", reloaded.Scene.Palette[111]);
        Assert.Equal(320, EditorPreferences.Parse("broken").CanvasWidth);
    }

    [Fact]
    public void MouseSmoothingReducesJitter()
    {
        var filter = new StrokeSmoother(new Point(0, 10)); var deviations = new List<double>();
        for (var x = 1; x <= 60; x++) deviations.Add(Math.Abs(filter.Add(new Point(x, 10 + (x % 2 == 0 ? 2 : -2))).Y - 10));
        Assert.True(deviations.Average() < 1);
    }

    [Fact]
    public void FreehandSelectionPreservesPixelsOutsideMaskAndClipsPastes()
    {
        var layer = ArtworkLayer.Create("Source", 10, 10);
        layer.SetPixel(2, 2, 3); layer.SetPixel(7, 7, 4);
        var selection = new ArtworkSelection(layer, 0, 0, 10, 10, [(0, 0), (9, 0), (0, 9)]);
        Assert.True(selection.Contains(2, 2)); Assert.False(selection.Contains(7, 7));
        selection.Clear(); Assert.Equal(-1, layer.Pixel(2, 2)); Assert.Equal(4, layer.Pixel(7, 7));
        var destination = ArtworkLayer.Create("Smaller", 4, 4);
        var copy = selection.Copy(destination); copy.Paste(); Assert.Equal(3, destination.Pixel(2, 2));
        copy.X = 2; copy.Y = 2; copy.Paste(); Assert.Equal(4, destination.Rows.Count);
    }

    [Fact]
    public async Task ToolOptionsRememberEachToolsSizeAndShape()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(true); var state = State(view);
            var window = new Window { Content = view, Width = 400, Height = 840 }; window.Show(); _ = Capture(window);
            Invoke(view, "ChooseTool", Tool.Smooth); Click(window, Named<Button>(window, "ToolOptions"));
            Named<NumericUpDown>(window, "ToolSize").Value = 12;
            Named<ComboBox>(window, "BrushTip").SelectedItem = BrushTip.Backslash;
            Click(window, Named<Button>(window, "ModalApply"));
            Invoke(view, "ChooseTool", Tool.Spray); Click(window, Named<Button>(window, "ToolOptions"));
            Named<NumericUpDown>(window, "ToolSize").Value = 30;
            Named<NumericUpDown>(window, "SprayDensity").Value = 60;
            SaveCapture(window, "COMIC_TOOL_OPTIONS");
            Click(window, Named<Button>(window, "ModalApply"));
            Invoke(view, "ChooseTool", Tool.Smooth); Assert.Equal(12, state.BrushSize); Assert.Equal(BrushTip.Backslash, state.Paint.Tip);
            Invoke(view, "ChooseTool", Tool.Spray); Assert.Equal(30, state.BrushSize); Assert.Equal(60, state.Paint.SprayDensity);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task TextCreationAndCanvasResizeUseRememberedSettings()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var state = State(view);
            var window = new Window { Content = view, Width = 1280, Height = 800 }; window.Show(); _ = Capture(window);
            Invoke(view, "AddText"); var original = state.SelectedText!;
            Invoke(view, "EditTextProperties", original); _ = Capture(window);
            Named<ComboBox>(window, "TextFont").SelectedIndex = 1;
            Named<NumericUpDown>(window, "TextFontSize").Value = 28;
            view.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "Bold")).IsChecked = true;
            Click(window, Named<Button>(window, "ModalApply"));
            Invoke(view, "AddText"); Assert.Equal(("google:Anton", 28d, true), (state.SelectedText!.FontId, state.SelectedText.FontSize, state.SelectedText.Bold));
            var canvas = view.GetVisualDescendants().OfType<CutsceneCanvas>().Single(c => c.Focusable);
            var start = canvas.TranslatePoint(new Point(235.5, 100.5), window)!.Value;
            var end = canvas.TranslatePoint(new Point(300.5, 155.5), window)!.Value;
            window.MouseDown(start, MouseButton.Left); window.MouseMove(end, RawInputModifiers.LeftMouseButton); window.MouseUp(end, MouseButton.Left);
            Assert.Equal(3, state.Frame.TextObjects.Count); Assert.Equal(28, state.Frame.TextObjects[^1].FontSize); Assert.True(state.Frame.TextObjects[^1].Bold);
            Invoke(view, "ResizeCanvas"); _ = Capture(window);
            Named<NumericUpDown>(window, "CanvasWidth").Value = 400; Named<NumericUpDown>(window, "CanvasHeight").Value = 200;
            Click(window, Named<Button>(window, "ModalApply")); Invoke(view, "New");
            Assert.Equal((400, 200), (state.Scene.Width, state.Scene.Height)); window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SelectionMoveCopyDeleteAndUndoPreserveArtwork()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var state = State(view);
            state.Layer.SetPixel(20, 20, 5); state.Layer.SetPixel(24, 24, 7); state.Tool = Tool.Select;
            var window = new Window { Content = view, Width = 1280, Height = 800 }; window.Show(); _ = Capture(window);
            var canvas = view.GetVisualDescendants().OfType<CutsceneCanvas>().Single(c => c.Focusable);
            Point At(int x, int y) => canvas.TranslatePoint(new Point(x + .5, y + .5), window)!.Value;
            void Drag(int x, int y, int dx, int dy) { window.MouseDown(At(x, y), MouseButton.Left); window.MouseMove(At(dx, dy), RawInputModifiers.LeftMouseButton); window.MouseUp(At(dx, dy), MouseButton.Left); }
            Drag(18, 18, 27, 27); Assert.False(state.CanUndo);
            Drag(20, 20, 40, 40); Assert.Equal(-1, state.Layer.Pixel(20, 20)); Assert.Equal(5, state.Layer.Pixel(40, 40));
            Invoke(view, "CopySelection", false); Invoke(view, "DeleteSelection"); Assert.Equal(-1, state.Layer.Pixel(40, 40));
            Invoke(view, "Undo"); Assert.Equal(5, state.Layer.Pixel(40, 40));
            Invoke(view, "PasteSelection"); Assert.Equal(5, state.Layer.Pixel(44, 44));
            Invoke(view, "Undo"); Invoke(view, "Undo"); Assert.Equal(5, state.Layer.Pixel(20, 20)); Assert.Equal(-1, state.Layer.Pixel(40, 40));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CurvesPolygonsAndSmoothedStrokesAreSingleUndoOperations()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var state = State(view);
            var window = new Window { Content = view, Width = 1280, Height = 800 }; window.Show(); _ = Capture(window);
            var canvas = view.GetVisualDescendants().OfType<CutsceneCanvas>().Single(c => c.Focusable);
            Point At(int x, int y) => canvas.TranslatePoint(new Point(x + .5, y + .5), window)!.Value;
            void Drag(int x, int y, int dx, int dy) { window.MouseDown(At(x, y), MouseButton.Left); window.MouseMove(At(dx, dy), RawInputModifiers.LeftMouseButton); window.MouseUp(At(dx, dy), MouseButton.Left); }
            Invoke(view, "ChooseTool", Tool.Curve); Drag(10, 40, 100, 40); Drag(25, 5, 25, 5); Drag(75, 75, 75, 75);
            Assert.Equal(0, state.Layer.Pixel(10, 40)); Assert.Equal(0, state.Layer.Pixel(100, 40));
            Invoke(view, "Undo"); Assert.All(state.Layer.Rows, row => Assert.All(row, c => Assert.Equal('F', c)));
            Invoke(view, "ChooseTool", Tool.Polygon); Drag(10, 10, 10, 10); Drag(50, 10, 50, 10); Drag(50, 50, 50, 50); Invoke(view, "FinishPath", false);
            Assert.Equal(0, state.Layer.Pixel(30, 10)); Assert.Equal(0, state.Layer.Pixel(30, 30)); Invoke(view, "Undo");
            Invoke(view, "ChooseTool", Tool.Pixel); state.Preferences.SmoothMouse = true; Drag(15, 15, 95, 65);
            Assert.Equal(0, state.Layer.Pixel(15, 15)); Assert.Equal(0, state.Layer.Pixel(95, 65)); Invoke(view, "Undo");
            Assert.All(state.Layer.Rows, row => Assert.All(row, c => Assert.Equal('F', c))); window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task IndexedExportUsesChosenLanguageAndOnlyVisibleColors()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            FontFixture.Register("Noto Sans JP");
            var scene = Cutscene.Create(160, 64); var frame = scene.Frames[0];
            frame.Layers[0].SetPixel(0, 0, 3); scene.Palette[4] = scene.Palette[3]; frame.Layers[0].SetPixel(1, 0, 4);
            var hidden = ArtworkLayer.Create("Hidden", 160, 64); hidden.Visible = false; hidden.SetPixel(2, 0, 5); frame.Layers.Add(hidden);
            frame.TextObjects.Add(new TextObject { Key = "message", X = 0, Y = 5, Width = 155, Height = 55, FontSize = 22, Color = 0 });
            scene.Translations["en"]["message"] = "English"; scene.Translations["ja"] = new() { ["message"] = "日本語" };
            using var english = new MemoryStream(); using var japanese = new MemoryStream();
            PngExporter.Write(english, scene, 0, "en"); PngExporter.Write(japanese, scene, 0, "ja");
            Assert.False(english.ToArray().SequenceEqual(japanese.ToArray()));
            Assert.InRange(BinaryPrimitives.ReadInt32BigEndian(japanese.ToArray().AsSpan(33)) / 3, 4, 256);
            japanese.Position = 0; using var decoded = new Bitmap(japanese); Assert.Equal(new PixelSize(160, 64), decoded.PixelSize);
            frame.TextVisible = false; using var artwork = new MemoryStream(); PngExporter.Write(artwork, scene, 0, "ja");
            Assert.Equal(2 * 3, BinaryPrimitives.ReadInt32BigEndian(artwork.ToArray().AsSpan(33)));
            var view = new MainView(); var state = State(view); state.Load(CutsceneFile.Write(scene)); state.Language = "en";
            var window = new Window { Content = view, Width = 1000, Height = 700 }; window.Show(); _ = Capture(window);
            Invoke(view, "Export", false); _ = Capture(window);
            var languages = Named<ComboBox>(window, "ExportLanguage"); Assert.Contains("ja", languages.Items.Cast<string>()); languages.SelectedItem = "ja";
            Assert.Equal("en", state.Language); Invoke(view, "CloseModal"); window.Close();
        }, CancellationToken.None);
    }
}
