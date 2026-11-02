using Avalonia.Controls;

namespace ComicEditor.Views;

public partial class MainView
{
    private void ShowColorMode()
    {
        if (editor.Scene.IsRgba)
        {
            ShowModal("Color mode: RGBA", new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    Label("RGBA keeps each pixel’s color and transparency, including partial opacity."),
                    Label("Automatic conversion to indexed color is not available: reducing colors or alpha could change your artwork. Use Undo to reverse a recent conversion, or File → New indexed cutscene to start a separate indexed document.")
                }
            });
            return;
        }
        ShowModal("Color mode: Indexed", new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Label($"Indexed is the compact default for new artwork. This document uses {editor.Scene.Palette.Count} palette colors and fully transparent pixels."),
                Label("Convert all frames and layers to RGBA to use partial opacity and independent pixel colors. Existing colors, text, audio, and transparency keep their appearance. You can undo the conversion."),
                Label("Conversion changes this document’s color mode, not just its file compression. RGBA is not guaranteed to make the file smaller.")
            }
        }, () => { CloseModal(); EnableRgbaMode(); }, "Convert to RGBA");
    }
}
