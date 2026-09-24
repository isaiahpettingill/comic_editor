using System.Globalization;
using System.Text;

namespace ComicEditor.Fonts;

public sealed record MissingFont(string Family, string Description, string Sample)
{
    public string Id => "google:" + Family;
    public string Link => "https://fonts.google.com/specimen/" + Uri.EscapeDataString(Family);
}

public static class LanguageFonts
{
    public static string CjkFamily(string language) => language.ToLowerInvariant() switch
    {
        var l when l.StartsWith("ja") => "Noto Sans JP",
        var l when l.StartsWith("ko") => "Noto Sans KR",
        var l when l.StartsWith("zh-hant") || l.StartsWith("zh-tw") || l.StartsWith("zh-hk") || l.StartsWith("zh-mo") => "Noto Sans TC",
        _ => "Noto Sans SC"
    };

    public static string? FamilyFor(Rune rune, string language) => rune.Value switch
    {
        >= 0x3040 and <= 0x30ff or >= 0x31f0 and <= 0x31ff => "Noto Sans JP",
        >= 0x1100 and <= 0x11ff or >= 0x3130 and <= 0x318f or >= 0xac00 and <= 0xd7af => "Noto Sans KR",
        >= 0x2e80 and <= 0x303f or >= 0x3100 and <= 0x312f or >= 0x3400 and <= 0x9fff or >= 0xf900 and <= 0xfaff or >= 0x20000 and <= 0x323af => CjkFamily(language),
        >= 0x0590 and <= 0x05ff or >= 0xfb1d and <= 0xfb4f => "Noto Sans Hebrew",
        >= 0x0530 and <= 0x058f => "Noto Sans Armenian",
        >= 0x10a0 and <= 0x10ff or >= 0x2d00 and <= 0x2d2f => "Noto Sans Georgian",
        >= 0x0600 and <= 0x06ff or >= 0x0750 and <= 0x077f or >= 0x08a0 and <= 0x08ff or >= 0xfb50 and <= 0xfdff or >= 0xfe70 and <= 0xfeff => "Noto Sans Arabic",
        >= 0x0900 and <= 0x097f or >= 0xa8e0 and <= 0xa8ff => "Noto Sans Devanagari",
        >= 0x0980 and <= 0x09ff => "Noto Sans Bengali",
        >= 0x0a00 and <= 0x0a7f => "Noto Sans Gurmukhi",
        >= 0x0a80 and <= 0x0aff => "Noto Sans Gujarati",
        >= 0x0b00 and <= 0x0b7f => "Noto Sans Oriya",
        >= 0x0b80 and <= 0x0bff => "Noto Sans Tamil",
        >= 0x0c00 and <= 0x0c7f => "Noto Sans Telugu",
        >= 0x0c80 and <= 0x0cff => "Noto Sans Kannada",
        >= 0x0d00 and <= 0x0d7f => "Noto Sans Malayalam",
        >= 0x0d80 and <= 0x0dff => "Noto Sans Sinhala",
        >= 0x0e00 and <= 0x0e7f => "Noto Sans Thai",
        >= 0x0e80 and <= 0x0eff => "Noto Sans Lao",
        >= 0x0f00 and <= 0x0fff => "Noto Serif Tibetan",
        >= 0x1000 and <= 0x109f or >= 0xaa60 and <= 0xaa7f => "Noto Sans Myanmar",
        >= 0x1200 and <= 0x137f => "Noto Sans Ethiopic",
        >= 0x1780 and <= 0x17ff => "Noto Sans Khmer",
        >= 0x1800 and <= 0x18af => "Noto Sans Mongolian",
        _ => null
    };

    public static bool NeedsGlyph(Rune rune) => !Rune.IsWhiteSpace(rune) &&
        Rune.GetUnicodeCategory(rune) is not (UnicodeCategory.Control or UnicodeCategory.Format) &&
        rune.Value is not (>= 0xfe00 and <= 0xfe0f or >= 0xe0100 and <= 0xe01ef);

    public static string Description(string family) => family switch
    {
        "Noto Sans SC" => "Chinese (Simplified)",
        "Noto Sans TC" => "Chinese (Traditional)",
        "Noto Sans JP" => "Japanese",
        "Noto Sans KR" => "Korean",
        _ => family.Replace("Noto Sans ", "").Replace("Noto Serif ", "")
    };
}
