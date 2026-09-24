using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaColorPicker;

namespace ComicEditor.Views;

internal static class PaletteColorPicker
{
    // Compose the upstream spectrum without its separate, global palette library.
    public static CustomColorPicker Create(Color color)
    {
        ColorPicker.TransitionsDisabled = true;
        return new CustomColorPicker
        {
            Name = "PaletteColorPicker",
            Color = color,
            Content = new Viewbox
            {
                MaxHeight = 220,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new ColorCanvasControls { IsAlphaVisible = false }
            }
        };
    }
}
