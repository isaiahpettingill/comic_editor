using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Input.Platform;
using ComicEditor.Format;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MediaDropResolvesFrameGeometryEvenWhenRouteSourceIsWorkspace(bool bottom)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var window = new Window { Content = view, Width = 1280, Height = 900 }; window.Show();
            var state = State(view); state.AddFrame(false); state.AddFrame(false); Invoke(view, "RefreshAll");
            if (bottom)
            {
                var pane = typeof(MainView).GetNestedType("Pane", System.Reflection.BindingFlags.NonPublic)!;
                Invoke(view, "MovePane", Enum.Parse(pane, "Storyboard"), 3);
            }
            _ = Capture(window);
            var root = (Grid)typeof(MainView).GetField("rootGrid", PrivateInstance)!.GetValue(view)!;
            var row = Named<Border>(window, "FrameRow1");
            var point = row.TranslatePoint(new Point(30, 30), root)!.Value;
            Assert.Same(state.Scene.Frames[1], Invoke(view, "DropFrameAt", point));
            var canvas = Named<ComicEditor.Views.DrawingViewport>(window, "CanvasViewport");
            Assert.Null(Invoke(view, "DropFrameAt", canvas.TranslatePoint(new Point(100, 100), root)!.Value));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DroppedWavConversionTargetsItsFrameAndKeepsOriginalBytes()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(async () =>
        {
            var view = new MainView(); var window = new Window { Content = view }; window.Show(); var state = State(view);
            var target = state.Frame; state.AddFrame(false); var active = state.Frame;
            var wav = WavAudio.Encode(new PcmAudio(new short[40001], 1, 8000));
            var path = Path.Combine(Path.GetTempPath(), "comic-drop-" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                await File.WriteAllBytesAsync(path, wav);
                var file = await window.StorageProvider.TryGetFileFromPathAsync(new Uri(path));
                await (Task)Invoke(view, "ImportFrameAudio", file!, state.Scene, target)!;
                Assert.True(target.AudioQoa.AsSpan().StartsWith("qoaf"u8));
                Assert.True(target.DurationMs >= 5001); Assert.Empty(active.AudioQoa);
                Assert.Equal(wav, await File.ReadAllBytesAsync(path));
                Assert.True(state.Undo()); Assert.Empty(state.Scene.Frames[0].AudioQoa);
            }
            finally { File.Delete(path); window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ImportingPngAddsUndoableLayerToCapturedFrameAndPreservesAlpha()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(async () =>
        {
            var view = new MainView(); var window = new Window { Content = view, Width = 1280, Height = 900 }; window.Show();
            var state = State(view); state.Scene.ConvertToRgba();
            state.Frame.Layers.Clear(); state.Frame.Layers.Add(ArtworkLayer.Create("Base", state.Scene.Width, state.Scene.Height, true));
            var frame = state.Frame;
            var bytes = RgbaPng.Encode(2, 1, [0xff000080, 0x00ff00ff]);
            await (Task)Invoke(view, "ImportImage", bytes, "sprite.png", state.Scene, frame, 2, 3)!;
            Assert.Equal(2, frame.Layers.Count);
            Assert.Equal(0xff000080u, frame.Layers[1].RgbaPixel(2, 3));
            Assert.Equal(0x00ff00ffu, frame.Layers[1].RgbaPixel(3, 3));
            Assert.True(state.Undo()); Assert.Single(state.Frame.Layers);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CorruptImageDoesNotCreateLayerOrUndoEntry()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(async () =>
        {
            var view = new MainView(); var window = new Window { Content = view }; window.Show(); var state = State(view);
            var revision = state.Revision;
            await Assert.ThrowsAsync<InvalidDataException>(async () => await (Task)Invoke(view, "ImportImage", new byte[] { 1, 2, 3 }, "bad.png", state.Scene, state.Frame, 0, 0)!);
            Assert.Single(state.Frame.Layers); Assert.Equal(revision, state.Revision);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ExternalBitmapClipboardCreatesNewLayerRatherThanInternalPaste()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(async () =>
        {
            var view = new MainView(); var window = new Window { Content = view }; window.Show();
            using var stream = new MemoryStream(RgbaPng.Encode(1, 1, [0xff0000ff])); using var bitmap = new Bitmap(stream);
            await window.Clipboard!.SetBitmapAsync(bitmap);
            Assert.True(await (Task<bool>)Invoke(view, "TryPasteExternalImage")!);
            Assert.Equal(2, State(view).Frame.Layers.Count);
            window.Close();
        }, CancellationToken.None);
    }
}
