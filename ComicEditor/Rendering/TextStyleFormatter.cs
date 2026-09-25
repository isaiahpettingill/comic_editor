using Avalonia.Media;
using ComicEditor.Format;

namespace ComicEditor.Rendering;

internal static class TextStyleFormatter
{
    public static void Apply(FormattedText formatted, TextObject obj, string language, int length)
    {
        foreach (var style in obj.Styles.Where(s => string.Equals(s.Language, language, StringComparison.OrdinalIgnoreCase)))
        {
            var start = Math.Clamp(style.Start, 0, length);
            var count = Math.Min(style.Length, length - start);
            if (count <= 0) continue;
            formatted.SetFontSize(style.FontSize, start, count);
            formatted.SetFontFamily(CutsceneFonts.Resolve(style.FontId, language), start, count);
        }
    }
}
