using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using ComicEditor.Format;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Theory]
    [InlineData(180, 320, 180)]
    [InlineData(190, 320, 180)]
    [InlineData(320, 320, 180)]
    [InlineData(190, 180, 320)]
    [InlineData(190, 640, 100)]
    public async Task StoryboardPreviewsUseAvailableWidthAndPreserveAspectRatio(int paneWidth, int width, int height)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var state = State(view);
            state.Load(CutsceneFile.Write(Cutscene.Create(width, height)));
            var window = new Window { Content = view, Width = 1280, Height = 800 };
            window.Show(); Invoke(view, "RefreshAll"); _ = Capture(window);
            var workspace = (Grid)typeof(MainView).GetField("desktopWorkspace", PrivateInstance)!.GetValue(view)!;
            workspace.ColumnDefinitions[0].Width = new GridLength(paneWidth);
            _ = Capture(window);

            var card = Named<Border>(window, "FrameRow0");
            var preview = Named<Viewbox>(window, "FramePreview0");
            var expectedWidth = Math.Min(card.Bounds.Width - 12, 220.0 * width / height);
            Assert.InRange(preview.Bounds.Width, expectedWidth - 1, expectedWidth + 1);
            Assert.InRange(preview.Bounds.Height, expectedWidth * height / width - 1, expectedWidth * height / width + 1);
            Assert.InRange(card.Bounds.Height - preview.Bounds.Height, 39, 41);
            AssertInside(window, Named<Button>(window, "FrameOptions0"));
            var actions = Named<Avalonia.Controls.Primitives.UniformGrid>(window, "StoryboardActions");
            Assert.InRange(actions.Bounds.Height, 33, 35);
            Assert.All(actions.Children, action => AssertInside(window, action));
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CanvasResizeUpdatesExistingPreviewAspectRatio()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var state = State(view);
            var window = new Window { Content = view, Width = 1280, Height = 800 };
            window.Show(); _ = Capture(window);
            var card = Named<Border>(window, "FrameRow0");
            var preview = Named<Viewbox>(window, "FramePreview0");
            var before = preview.Bounds.Height;

            state.Scene.ResizeCanvas(320, 320, false);
            Invoke(view, "RefreshAll"); _ = Capture(window);
            Assert.Same(card, Named<Border>(window, "FrameRow0"));
            Assert.Same(preview, Named<Viewbox>(window, "FramePreview0"));
            Assert.True(preview.Bounds.Height > before);
            Assert.InRange(Math.Abs(preview.Bounds.Width - preview.Bounds.Height), 0, 1);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SelectingStoryboardCardsKeepsPreviewSizeAndFitsLongMetadata()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var state = State(view);
            state.AddFrame(false);
            state.Scene.Frames[0].DurationMs = 3600000;
            state.Scene.Frames[0].Requirement = "not A_VERY_LONG_VARIABLE_NAME_THAT_SHOULD_BE_TRUNCATED";
            var window = new Window { Content = view, Width = 1280, Height = 800 };
            window.Show(); Invoke(view, "RefreshAll"); _ = Capture(window);
            var card = Named<Border>(window, "FrameRow0");
            var preview = Named<Viewbox>(window, "FramePreview0");
            var beforeCard = card.Bounds; var beforePreview = preview.Bounds;

            Click(window, preview);
            Assert.Equal(0, state.FrameIndex);
            Assert.Equal(beforeCard, card.Bounds);
            Assert.Equal(beforePreview, preview.Bounds);
            var options = Named<Button>(window, "FrameOptions0");
            var requirement = Named<TextBlock>(window, "FrameRequirement0");
            Assert.True(requirement.Bounds.Width > 0);
            Assert.True(requirement.TranslatePoint(new Point(requirement.Bounds.Width, 0), options)!.Value.X <= 0);
            Click(window, options);
            Assert.True(options.ContextMenu!.IsOpen);
            options.ContextMenu.Close();
            Click(window, Named<Viewbox>(window, "FramePreview1"));
            Assert.Equal(1, state.FrameIndex);
            Assert.Equal(beforeCard, card.Bounds);
            window.Close();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(320, 568)]
    [InlineData(400, 840)]
    [InlineData(940, 400)]
    public async Task CompactStoryboardUsesLargePreviewsAndTouchSizedOptions(int width, int height)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(touchLayout: true);
            var window = new Window { Content = view, Width = width, Height = height };
            window.Show(); Click(window, Named<Button>(window, "CompactFrames"));
            var preview = Named<Viewbox>(window, "FramePreview0");
            var options = Named<Button>(window, "FrameOptions0");
            Assert.True(preview.Bounds.Width >= 280);
            Assert.InRange(preview.Bounds.Height, 150, 260);
            Assert.True(options.Bounds.Width >= 44 && options.Bounds.Height >= 44);
            Click(window, Named<Button>(window, "CompactDraw"));
            Assert.False(preview.IsEffectivelyVisible);
            Click(window, Named<Button>(window, "CompactFrames"));
            Assert.True(preview.IsEffectivelyVisible);
            window.Close();
        }, CancellationToken.None);
    }
}
