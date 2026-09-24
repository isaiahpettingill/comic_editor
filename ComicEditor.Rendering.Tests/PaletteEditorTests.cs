using Avalonia.Controls;
using Avalonia.Headless;
using ComicEditor.Editing;
using ComicEditor.Format;
using ComicEditor.Rendering;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PaletteEditorKeepsProjectAndLibraryIndependent(bool touch)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var files = new Dictionary<string, string> { ["Two.gpl"] = "GIMP Palette\nName: Two\n0 0 0 Black\n255 255 255 White\n" };
            PaletteLibrary.List = () => Task.FromResult(files.Keys.ToArray());
            PaletteLibrary.Read = file => Task.FromResult(files[file]);
            PaletteLibrary.Write = (file, text) => { files[file] = text; return Task.CompletedTask; };
            var preset = files["Two.gpl"];
            var view = new MainView(touch); var window = new Window { Content = view, Width = touch ? 320 : 1000, Height = touch ? 568 : 800 };
            window.Show(); _ = Capture(window); var editor = State(view); var original = editor.Scene.Palette.ToArray();
            Invoke(view, "EditPalette"); _ = Capture(window);
            Click(window, Named<Button>(window, "LoadPalettePreset"));
            Assert.Equal(2, Named<WrapPanel>(window, "PaletteEditorSwatches").Children.Count);
            Named<TextBox>(window, "PaletteEditorHex").Text = "#112233"; _ = Capture(window);
            Assert.Equal(original, editor.Scene.Palette); Assert.Equal(preset, files["Two.gpl"]);
            AssertInside(window, Named<Border>(window, "ModalCard"));
            SaveCapture(window, touch ? "COMIC_PALETTE_MOBILE" : "COMIC_PALETTE_DESKTOP");
            Click(window, Named<Button>(window, "ModalApply"));
            Assert.Equal(new[] { "#112233", "#FFFFFF" }, editor.Scene.Palette);
            Assert.Equal(preset, files["Two.gpl"]);
            Assert.Equal(editor.Scene.Palette, CutsceneFile.Parse(CutsceneFile.Write(editor.Scene)).Palette);
            Invoke(view, "EditPalette"); _ = Capture(window);
            Named<TextBox>(window, "PaletteName").Text = "Local copy";
            Click(window, Named<Button>(window, "SavePalettePreset")); Assert.Equal(editor.Scene.Palette, GplPalette.Parse(files["Local copy.gpl"]).Colors.Select(c => c.Hex));
            Named<NumericUpDown>(window, "PaletteSize").Value = 255; _ = Capture(window);
            Assert.Equal(255, Named<WrapPanel>(window, "PaletteEditorSwatches").Children.Count);
            Invoke(view, "CloseModal"); Assert.Equal(2, editor.Scene.Palette.Count); // Cancel leaves the project alone.
            Invoke(view, "Undo"); Assert.Equal(original, editor.Scene.Palette);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task UndoPaletteChangeClearsArtworkClipboardWithObsoleteIndices()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var window = new Window { Content = view, Width = 1000, Height = 800 };
            window.Show(); var editor = State(view);
            editor.ApplyPalette(["#000000", "#FFFFFF"]);
            editor.ApplyPalette(Enumerable.Repeat("#000000", 255).ToArray());
            editor.Layer.SetPixel(0, 0, 254);
            Invoke(view, "SelectAllArtwork"); Invoke(view, "CopySelection", false);
            Invoke(view, "Undo"); Assert.Equal(2, editor.Scene.Palette.Count);
            Invoke(view, "PasteSelection"); editor.Scene.Validate();
            Assert.Equal(-1, editor.Layer.Pixel(0, 0));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CompileSupportsColor254AndTransparencyWith255Colors()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var scene = Cutscene.Create(2, 1); scene.Palette = Enumerable.Range(0, 255).Select(i => $"#{i:X6}").ToList();
            scene.Frames[0].Layers[0].SetPixel(0, 0, 254);
            var output = DisplayCompiler.Compile(scene);
            Assert.Equal(2u, output.Version); Assert.Equal(255, output.PaletteRgb.Count);
            Assert.Equal(254, output.Frames[0].IndexedArtwork[0]); Assert.Equal(255, output.Frames[0].IndexedArtwork[1]);
            using var png = new MemoryStream(); PngExporter.Write(png, scene, 0, "en"); Assert.True(png.Length > 0);
        }, CancellationToken.None);
    }
}

public sealed class PaletteLibraryTests
{
    [Fact]
    public async Task LibraryStoresRealGplFilesAndRejectsEscapingItsFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "comic-palette-test-" + Guid.NewGuid().ToString("N"));
        var library = new PaletteLibrary(root);
        try
        {
            Assert.Empty(library.Files()); var text = "GIMP Palette\nName: Test\n0 0 0\n255 255 255\n";
            await library.Save("Test.gpl", text); Assert.Equal(new[] { "Test.gpl" }, library.Files()); Assert.Equal(text, await library.Load("Test.gpl"));
            await Assert.ThrowsAsync<ArgumentException>(() => library.Save("../escape.gpl", text));
            await Assert.ThrowsAsync<InvalidDataException>(() => library.Save("Test.gpl", "bad"));
            Assert.Equal(text, await library.Load("Test.gpl"));
            Assert.DoesNotContain('/', PaletteLibrary.FileName("../../Hi")); Assert.Equal("Palette-CON.gpl", PaletteLibrary.FileName("CON"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ReducingPaletteRemapsRemovedSlotsWithoutLosingTransparencyAndIsUndoable()
    {
        var editor = new EditorState(); var palette = Enumerable.Repeat("#000000", 255).ToArray(); palette[1] = palette[254] = "#FFFFFF";
        editor.ApplyPalette(palette); editor.Layer.SetPixel(0, 0, 254);
        editor.Frame.TextObjects.Add(new TextObject { Color = 254 });
        editor.AddFrame(true); editor.Layer.Visible = false;
        var before = CutsceneFile.Write(editor.Scene);
        editor.Color = 254; editor.ApplyPalette(["#000000", "#FFFFFF"]);
        Assert.Equal(1, editor.Color);
        foreach (var frame in editor.Scene.Frames)
        { Assert.Equal(1, frame.Layers[0].Pixel(0, 0)); Assert.Equal(-1, frame.Layers[0].Pixel(1, 0)); Assert.Equal(1, frame.TextObjects[0].Color); }
        Assert.True(editor.Undo()); Assert.Equal(before, CutsceneFile.Write(editor.Scene));
        Assert.True(editor.Redo()); Assert.Equal(2, editor.Scene.Palette.Count);
        editor.New(); Assert.Equal(2, editor.Scene.Palette.Count);
    }
}
