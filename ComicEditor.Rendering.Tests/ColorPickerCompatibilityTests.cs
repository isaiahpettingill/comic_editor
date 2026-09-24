using Avalonia.Controls;
using Avalonia.Headless;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Fact]
    public async Task RequestedColorPickerCanRender()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var picker = ComicEditor.Views.PaletteColorPicker.Create(Avalonia.Media.Colors.Red);
            var window = new Window { Content = picker, Width = 600, Height = 600 };
            window.Show(); Assert.True(Capture(window).Length > 1000); window.Close();
        }, CancellationToken.None);
    }
}
