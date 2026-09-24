using Avalonia.Controls;
using Avalonia.Headless;
using ComicEditor.Updating;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Theory]
    [InlineData(320, 568)]
    [InlineData(1280, 800)]
    public async Task SoftwareUpdatesDialogFitsAndDevelopmentBuildsStayDisabled(int width, int height)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            Assert.Null(UpdateHost.Installation);
            var view = new MainView(width < 900);
            var window = new Window { Content = view, Width = width, Height = height }; window.Show(); _ = Capture(window);
            Invoke(view, "ShowUpdates"); _ = Capture(window);
            AssertInside(window, Named<Border>(window, "ModalCard"));
            var action = Named<Button>(window, "UpdateAction"); Assert.False(action.IsEnabled);
            var check = Named<CheckBox>(window, "AutoCheckUpdates"); Assert.False(check.IsEnabled);
            Assert.Contains("stable releases", Named<TextBlock>(window, "UpdateStatus").Text);
            SaveCapture(window, width < 900 ? "COMIC_UPDATE_MOBILE" : "COMIC_UPDATE_DESKTOP");
            Invoke(view, "CloseModal"); window.Close();
        }, CancellationToken.None);
    }
}
