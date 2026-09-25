namespace ComicEditor.Format;

/// <summary>Straight-alpha color packed as 0xRRGGBBAA.</summary>
public static class RgbaColor
{
    public static bool IsHex(string? value) => value is { Length: 7 or 9 } && value[0] == '#' && value[1..].All(Uri.IsHexDigit);
    public static uint Parse(string value)
    {
        if (!IsHex(value)) throw new FormatException("Expected #RRGGBB or #RRGGBBAA.");
        var rgb = Convert.ToUInt32(value[1..], 16);
        return value.Length == 7 ? rgb << 8 | 255 : rgb;
    }
    public static string Hex(uint value) => $"#{value:X8}";
    public static uint Blend(uint foreground, uint background, int opacity = 255)
    {
        var sa = (int)(foreground & 255) * Math.Clamp(opacity, 0, 255) / 255;
        if (sa == 0) return background;
        var ba = (int)(background & 255);
        var inverse = 255 - sa;
        var outAlpha = sa + (ba * inverse + 127) / 255;
        if (outAlpha == 0) return 0;
        static uint Channel(uint front, uint back, int shift, int sa, int ba, int inverse, int oa)
        {
            var f = (int)((front >> shift) & 255); var b = (int)((back >> shift) & 255);
            return (uint)((f * sa * 255 + b * ba * inverse + oa * 127) / (oa * 255));
        }
        return Channel(foreground, background, 24, sa, ba, inverse, outAlpha) << 24 |
            Channel(foreground, background, 16, sa, ba, inverse, outAlpha) << 16 |
            Channel(foreground, background, 8, sa, ba, inverse, outAlpha) << 8 | (uint)outAlpha;
    }
}
