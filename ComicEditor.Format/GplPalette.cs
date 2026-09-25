using System.Globalization;
using System.Text;

namespace ComicEditor.Format;

// GIMP's textual sRGB palette format. Entry order is the palette index.
public sealed record GplColor(string Hex, string Name = "");
public sealed record GplPalette(string Name, IReadOnlyList<GplColor> Colors, int Columns = 16)
{
    public const int Capacity = 65535;
    public static bool IsHex(string? text) => RgbaColor.IsHex(text);

    public static GplPalette Parse(string text, string fallbackName = "Imported palette")
    {
        if (text.Length > 8 * 1024 * 1024) throw new InvalidDataException("The palette file is too large.");
        using var reader = new StringReader(text.TrimStart('\uFEFF'));
        if (reader.ReadLine()?.TrimEnd() != "GIMP Palette") throw new InvalidDataException("This is not a GIMP palette (.gpl) file.");
        var name = fallbackName; var columns = 0; var colors = new List<GplColor>(); var alpha = new Dictionary<int, byte>(); var number = 1;
        while (reader.ReadLine() is { } raw)
        {
            number++; var line = raw.Trim();
            if (line.StartsWith("# ComicEditor-Alpha:", StringComparison.Ordinal))
            {
                var alphaParts = line[20..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (alphaParts.Length == 2 && int.TryParse(alphaParts[0], out var slot) && slot >= 0 && byte.TryParse(alphaParts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)) alpha[slot] = value;
                continue;
            }
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
            if (colors.Count > Capacity) throw new InvalidDataException("This palette has more than 65535 colors.");
        }
        if (colors.Count < 2) throw new InvalidDataException("A cutscene palette needs at least two colors.");
        foreach (var (slot, value) in alpha)
            if (slot < colors.Count) colors[slot] = colors[slot] with { Hex = colors[slot].Hex + value.ToString("X2") };
        return new(string.IsNullOrWhiteSpace(name) ? fallbackName : name, colors, columns);
    }

    public string Write()
    {
        if (Colors.Count is < 2 or > Capacity || Columns is < 0 or > 255 || Colors.Any(c => !IsHex(c.Hex)))
            throw new InvalidDataException("A palette needs 2–65535 valid RGB or RGBA colors.");
        static string SingleLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        var output = new StringBuilder("GIMP Palette\nName: ").Append(SingleLine(Name)).Append("\nColumns: ")
            .Append(Columns.ToString(CultureInfo.InvariantCulture)).Append("\n#\n");
        for (var i = 0; i < Colors.Count; i++)
        {
            var color = Colors[i];
            var bytes = Convert.FromHexString(color.Hex[1..7]);
            output.Append(CultureInfo.InvariantCulture, $"{bytes[0],3} {bytes[1],3} {bytes[2],3}\t{SingleLine(color.Name)}\n");
            if (color.Hex.Length == 9) output.Append(CultureInfo.InvariantCulture, $"# ComicEditor-Alpha: {i} {color.Hex[7..]}\n");
        }
        return output.ToString();
    }

}
