using System.Globalization;
using System.Text;

namespace ComicEditor.Format;

// GIMP's textual sRGB palette format. Entry order is the palette index.
public sealed record GplColor(string Hex, string Name = "");
public sealed record GplPalette(string Name, IReadOnlyList<GplColor> Colors, int Columns = 16)
{
    public const int Capacity = 255;
    public static bool IsHex(string? text) => text is { Length: 7 } && text[0] == '#' && text[1..].All(Uri.IsHexDigit);

    public static GplPalette Parse(string text, string fallbackName = "Imported palette")
    {
        if (text.Length > 1_048_576) throw new InvalidDataException("The palette file is too large.");
        using var reader = new StringReader(text.TrimStart('\uFEFF'));
        if (reader.ReadLine()?.TrimEnd() != "GIMP Palette") throw new InvalidDataException("This is not a GIMP palette (.gpl) file.");
        var name = fallbackName; var columns = 0; var colors = new List<GplColor>(); var number = 1;
        while (reader.ReadLine() is { } raw)
        {
            number++; var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (colors.Count == 0 && line.StartsWith("Name:", StringComparison.Ordinal)) { name = line[5..].Trim(); continue; }
            if (colors.Count == 0 && line.StartsWith("Columns:", StringComparison.Ordinal))
            {
                if (!int.TryParse(line[8..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out columns) || columns is < 0 or > 255)
                    throw new InvalidDataException($"Line {number}: Columns must be between 0 and 255.");
                continue;
            }
            var parts = line.Split((char[]?)null, 4, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || !byte.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var r) ||
                !byte.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var g) ||
                !byte.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var b))
                throw new InvalidDataException($"Line {number}: expected red, green, and blue values from 0 to 255.");
            colors.Add(new($"#{r:X2}{g:X2}{b:X2}", parts.Length == 4 ? parts[3].Trim() : ""));
            if (colors.Count > Capacity) throw new InvalidDataException("This palette has more than 255 colors. One index is reserved for transparency; no colors were imported.");
        }
        if (colors.Count < 2) throw new InvalidDataException("A cutscene palette needs at least two colors.");
        return new(string.IsNullOrWhiteSpace(name) ? fallbackName : name, colors, columns);
    }

    public string Write()
    {
        if (Colors.Count is < 2 or > Capacity || Columns is < 0 or > 255 || Colors.Any(c => !IsHex(c.Hex)))
            throw new InvalidDataException("A palette needs 2–255 valid RGB colors.");
        static string SingleLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        var output = new StringBuilder("GIMP Palette\nName: ").Append(SingleLine(Name)).Append("\nColumns: ")
            .Append(Columns.ToString(CultureInfo.InvariantCulture)).Append("\n#\n");
        foreach (var color in Colors)
        {
            var bytes = Convert.FromHexString(color.Hex[1..]);
            output.Append(CultureInfo.InvariantCulture, $"{bytes[0],3} {bytes[1],3} {bytes[2],3}\t{SingleLine(color.Name)}\n");
        }
        return output.ToString();
    }

}
