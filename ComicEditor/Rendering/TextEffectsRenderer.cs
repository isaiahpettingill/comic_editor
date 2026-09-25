using System.Globalization;
using Avalonia;
using Avalonia.Media;
using ComicEditor.Format;

namespace ComicEditor.Rendering;

internal static class TextEffectsRenderer
{
    public static bool HasEffect(TextObject obj) => obj.RotationDegrees != 0 || obj.CurveDegrees != 0 || obj.SizeEffect != TextSizeEffect.Normal;

    public static Rect Bounds(TextObject obj, TextPlacement placement)
    {
        var angle = Math.Abs(obj.CurveDegrees * Math.PI / 180);
        var bend = angle == 0 ? 0 : placement.Width / angle * (1 - Math.Cos(angle / 2));
        var maxFont = Math.Max(obj.FontSize, obj.Styles.Select(s => s.FontSize).DefaultIfEmpty(obj.FontSize).Max());
        var radius = Math.Sqrt(placement.Width * placement.Width + placement.Height * placement.Height) / 2 + bend + maxFont * 2 +
            Math.Abs(obj.CurveAnchorX * placement.Width) + Math.Abs(obj.CurveAnchorY * placement.Height);
        return new Rect(placement.X + placement.Width / 2 - radius, placement.Y + placement.Height / 2 - radius,
            radius * 2, radius * 2);
    }

    public static void Draw(DrawingContext context, TextObject obj, TextPlacement placement, string text,
        string language, string styleLanguage, IBrush ink)
    {
        if (!HasEffect(obj))
        {
            var culture = CutsceneCanvas.Culture(language);
            var formatted = new FormattedText(text, culture,
                culture.TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                new Typeface(CutsceneFonts.Resolve(obj.FontId, language), obj.Italic ? FontStyle.Italic : FontStyle.Normal,
                    obj.Bold ? FontWeight.Bold : FontWeight.Normal), obj.FontSize, ink)
            { MaxTextWidth = placement.Width };
            TextStyleFormatter.Apply(formatted, obj, styleLanguage, text.Length);
            using (context.PushClip(new Rect(placement.X, placement.Y, placement.Width, placement.Height)))
                context.DrawText(formatted, new Point(placement.X, placement.Y));
            return;
        }

        var center = new Point(placement.X + placement.Width / 2, placement.Y + placement.Height / 2);
        var rotation = obj.RotationDegrees * Math.PI / 180;
        using var rotated = context.PushTransform(Matrix.CreateTranslation(-center.X, -center.Y) *
            Matrix.CreateRotation(rotation) * Matrix.CreateTranslation(center.X, center.Y));
        var elements = new List<(string Value, int Index)>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext()) elements.Add(((string)enumerator.Current!, enumerator.ElementIndex));
        var count = Math.Max(1, elements.Count(e => e.Value != "\n"));
        var spans = obj.Styles.Where(s => string.Equals(s.Language, styleLanguage, StringComparison.OrdinalIgnoreCase)).ToArray();
        var drawn = 0;
        var x = 0d; var y = 0d; var lineHeight = obj.FontSize * 1.5;
        var cultureInfo = CutsceneCanvas.Culture(language);
        foreach (var (value, index) in elements)
        {
            if (y > placement.Height + obj.FontSize) break;
            if (value is "\n" or "\r\n") { x = 0; y += lineHeight; lineHeight = obj.FontSize * 1.5; continue; }
            var progress = count == 1 ? .5 : (double)drawn / (count - 1);
            drawn++;
            var factor = obj.SizeEffect switch
            {
                TextSizeEffect.Grow => .6 + .8 * progress,
                TextSizeEffect.Shrink => 1.4 - .8 * progress,
                TextSizeEffect.GrowThenShrink => .6 + .8 * (1 - Math.Abs(2 * progress - 1)),
                _ => 1d
            };
            var span = spans.LastOrDefault(s => index >= s.Start && index < s.Start + s.Length);
            var fontId = span?.FontId ?? obj.FontId;
            var size = (span?.FontSize ?? obj.FontSize) * factor;
            var glyph = new FormattedText(value, cultureInfo,
                cultureInfo.TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                new Typeface(CutsceneFonts.Resolve(fontId, language), obj.Italic ? FontStyle.Italic : FontStyle.Normal,
                    obj.Bold ? FontWeight.Bold : FontWeight.Normal), size, ink);
            var advance = Math.Max(glyph.WidthIncludingTrailingWhitespace, size * .25);
            if (x > 0 && x + advance > placement.Width) { x = 0; y += lineHeight; lineHeight = obj.FontSize * 1.5; }
            lineHeight = Math.Max(lineHeight, glyph.Height);
            var glyphCenter = new Point(placement.X + x + advance / 2, placement.Y + y + glyph.Height / 2);
            var anchorX = placement.X + placement.Width * (.5 + obj.CurveAnchorX);
            var angle = obj.CurveDegrees * Math.PI / 180 * (glyphCenter.X - anchorX) / Math.Max(1, placement.Width);
            var radius = obj.CurveDegrees == 0 ? 0 : placement.Width / Math.Abs(obj.CurveDegrees * Math.PI / 180);
            var signedRadius = Math.CopySign(radius, obj.CurveDegrees);
            var target = obj.CurveDegrees == 0 ? glyphCenter : new Point(
                anchorX + signedRadius * Math.Sin(angle),
                glyphCenter.Y + signedRadius * (1 - Math.Cos(angle)) + obj.CurveAnchorY * placement.Height);
            using (context.PushTransform(Matrix.CreateTranslation(-glyphCenter.X, -glyphCenter.Y) *
                Matrix.CreateRotation(angle) * Matrix.CreateTranslation(target.X, target.Y)))
                context.DrawText(glyph, new Point(placement.X + x, placement.Y + y));
            x += advance;
        }
    }
}
