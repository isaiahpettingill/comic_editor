namespace ComicEditor.Format;

// Version 3 supports 2–255 colors. Hex rows still use FF for transparency.
// Protobuf stores the same index as one byte per pixel on disk.
public sealed class Cutscene
{
    public int Version { get; set; } = 3;
    public bool IsRgba => Version == 4;
    public int Width { get; set; } = 320;
    public int Height { get; set; } = 180;
    public string FallbackLanguage { get; set; } = "en";
    public List<string> FallbackFontIds { get; set; } = [];
    public List<string> Palette { get; set; } = DefaultPalette();

    public static List<string> DefaultPalette()
    {
        var colors = new List<string> {
        "#17171D", "#FFFFFF", "#FFCCAA", "#E86A73",
        "#A44575", "#623A6F", "#394D76", "#4D83A3",
        "#64B5AD", "#A0C992", "#DCE7A1", "#F4D07D",
        "#EBA668", "#A97763", "#6D5A60", "#A7A9BA"
        };
        for (var hue = 0; hue < 16; hue++)
            for (var shade = 0; shade < 7; shade++)
            {
                var h = hue * 360.0 / 16;
                var saturation = 0.35 + (shade % 2) * 0.4;
                var value = 0.35 + (shade / 2) * 0.2;
                var chroma = value * saturation;
                var x = chroma * (1 - Math.Abs(h / 60 % 2 - 1));
                var m = value - chroma;
                var (r, g, b) = h switch
                {
                    < 60 => (chroma, x, 0.0),
                    < 120 => (x, chroma, 0.0),
                    < 180 => (0.0, chroma, x),
                    < 240 => (0.0, x, chroma),
                    < 300 => (x, 0.0, chroma),
                    _ => (chroma, 0.0, x)
                };
                colors.Add($"#{(int)Math.Round((r + m) * 255):X2}{(int)Math.Round((g + m) * 255):X2}{(int)Math.Round((b + m) * 255):X2}");
            }
        return colors;
    }
    public List<Frame> Frames { get; set; } = [];
    public Dictionary<string, Dictionary<string, string>> Translations { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new(),
        ["es"] = new(),
        ["pt"] = new()
    };

    public static Cutscene Create(int width = 320, int height = 180, bool rgba = false)
    {
        var scene = new Cutscene
        {
            Width = width,
            Height = height,
            Version = rgba ? 4 : 3,
            Palette = rgba ? DefaultPalette().Select(c => c + "FF").ToList() : DefaultPalette()
        };
        scene.Frames.Add(Frame.Create(width, height, rgba));
        return scene;
    }

    public void ConvertToRgba()
    {
        if (IsRgba) return;
        Validate();
        var colors = Palette.Select(RgbaColor.Parse).ToArray();
        foreach (var layer in Frames.SelectMany(frame => frame.Layers))
        {
            var rows = new List<string>(Height);
            for (var y = 0; y < Height; y++)
            {
                var row = new char[Width * 8];
                for (var x = 0; x < Width; x++)
                {
                    var index = layer.Pixel(x, y);
                    var color = index >= 0 ? colors[index] : 0u;
                    RgbaColor.Hex(color).AsSpan(1).CopyTo(row.AsSpan(x * 8, 8));
                }
                rows.Add(new string(row));
            }
            layer.Rows = rows; layer.IsRgba = true;
        }
        Version = 4;
        Palette = Palette.Select(color => color + "FF").ToList();
    }

    public string Text(string language, string key) =>
        Translations.TryGetValue(language, out var entries) && entries.TryGetValue(key, out var text) ? text : "";

    public string RenderText(string language, string key)
    {
        var text = Text(language, key);
        return string.IsNullOrWhiteSpace(text) ? Text(FallbackLanguage, key) : text;
    }

    public void ResizeCanvas(int width, int height, bool center)
    {
        if (width is < 1 or > 2048 || height is < 1 or > 2048)
            throw new ArgumentOutOfRangeException(nameof(width), "Canvas dimensions must be between 1 and 2048 pixels.");
        var dx = center ? (int)Math.Floor((width - Width) / 2.0) : 0;
        var dy = center ? (int)Math.Floor((height - Height) / 2.0) : 0;
        foreach (var frame in Frames)
        {
            foreach (var layer in frame.Layers)
            {
                var rows = new List<string>(height);
                var sourceX = Math.Max(0, -dx);
                var destX = Math.Max(0, dx);
                var count = Math.Min(Width - sourceX, width - destX);
                for (var y = 0; y < height; y++)
                {
                    var stride = layer.PixelStride;
                    var row = new string('0', width * stride).ToCharArray();
                    if (!layer.IsRgba) Array.Fill(row, 'F');
                    var sourceY = y - dy;
                    if (sourceY >= 0 && sourceY < Height && count > 0)
                        layer.Rows[sourceY].CopyTo(sourceX * stride, row, destX * stride, count * stride);
                    rows.Add(new string(row));
                }
                layer.Rows = rows;
            }
            foreach (var text in frame.TextObjects)
            {
                text.X += dx; text.Y += dy;
                foreach (var placement in text.Placements.Values) { placement.X += dx; placement.Y += dy; }
            }
        }
        Width = width; Height = height;
    }

    public void Validate()
    {
        if (Version is not (3 or 4) || Width is < 1 or > 2048 || Height is < 1 or > 2048 || Palette.Count < 2 || Palette.Count > (IsRgba ? 65535 : 255) || Frames.Count == 0)
            throw new InvalidDataException("Unsupported cutscene dimensions, version, palette, or empty storyboard.");
        if (Palette.Any(p => !RgbaColor.IsHex(p) || p.Length != (IsRgba ? 9 : 7)))
            throw new InvalidDataException(IsRgba ? "Palette entries must be #RRGGBBAA colors." : "Palette entries must be #RRGGBB colors.");
        if (FallbackFontIds.Count > 128 || FallbackFontIds.Any(id => !id.StartsWith("google:", StringComparison.Ordinal) || id.Length is < 8 or > 107 || !id[7..].All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-')))
            throw new InvalidDataException("Fallback fonts must be Google Fonts family references.");
        foreach (var frame in Frames)
        {
            if (frame.Layers.Count == 0) throw new InvalidDataException("Every frame needs an artwork layer.");
            foreach (var layer in frame.Layers)
            {
                if (layer.IsRgba != IsRgba || layer.Rows.Count != Height)
                    throw new InvalidDataException("Artwork must contain one valid palette byte per pixel.");
                foreach (var row in layer.Rows)
                {
                    if (row.Length != Width * layer.PixelStride) throw new InvalidDataException("Artwork has an invalid pixel count.");
                    if (IsRgba) { if (!row.All(Uri.IsHexDigit)) throw new InvalidDataException("Artwork contains invalid RGBA pixels."); continue; }
                    for (var x = 0; x < Width; x++)
                    {
                        var high = HexDigit(row[x * 2]); var low = HexDigit(row[x * 2 + 1]);
                        var index = high * 16 + low;
                        if (high < 0 || low < 0 || index != 255 && index >= Palette.Count)
                            throw new InvalidDataException("Artwork must contain one valid palette byte per pixel.");
                    }
                }
            }
            foreach (var obj in frame.TextObjects)
            {
                if (string.IsNullOrWhiteSpace(obj.Key) || obj.Width <= 0 || obj.Height <= 0 || obj.FontSize <= 0 ||
                    !double.IsFinite(obj.X) || !double.IsFinite(obj.Y) || !double.IsFinite(obj.Width) ||
                    !double.IsFinite(obj.Height) || !double.IsFinite(obj.FontSize) || obj.Color < 0 || obj.Color >= Palette.Count)
                    throw new InvalidDataException("Invalid text object.");
                if (obj.Placements.Count > 256 || obj.Placements.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Key.Length > 32 ||
                    !double.IsFinite(p.Value.X) || !double.IsFinite(p.Value.Y) || !double.IsFinite(p.Value.Width) ||
                    !double.IsFinite(p.Value.Height) || p.Value.Width <= 0 || p.Value.Height <= 0))
                    throw new InvalidDataException("Invalid per-language text placement.");
                if (obj.Styles.Count > 10000 || obj.Styles.Any(s => string.IsNullOrWhiteSpace(s.Language) || s.Language.Length > 32 || s.Length < 1 ||
                    s.Start < 0 || s.Start > 1_000_000 || s.Length > 1_000_000 - s.Start || s.FontSize is < 1 or > 2048 ||
                    !double.IsFinite(s.FontSize) || string.IsNullOrWhiteSpace(s.FontId)))
                    throw new InvalidDataException("Invalid highlighted text style.");
            }
        }
    }

    private static int HexDigit(char value) => value switch
    {
        >= '0' and <= '9' => value - '0',
        >= 'A' and <= 'F' => value - 'A' + 10,
        >= 'a' and <= 'f' => value - 'a' + 10,
        _ => -1
    };
}

public sealed class Frame
{
    public bool TextVisible { get; set; } = true;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public List<ArtworkLayer> Layers { get; set; } = [];
    public List<TextObject> TextObjects { get; set; } = [];

    public static Frame Create(int width, int height, bool rgba = false) => new()
    {
        Layers = [ArtworkLayer.Create("Artwork", width, height, rgba)]
    };
}

public sealed class ArtworkLayer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Artwork";
    public bool Visible { get; set; } = true;
    public List<string> Rows { get; set; } = [];
    public bool IsRgba { get; set; }
    public int PixelStride => IsRgba ? 8 : 2;
    public int Width => Rows.Count == 0 ? 0 : Rows[0].Length / PixelStride;

    public static ArtworkLayer Create(string name, int width, int height, bool rgba = false) => new()
    {
        Name = name,
        IsRgba = rgba,
        Rows = Enumerable.Repeat(rgba ? new string('0', width * 8) : string.Concat(Enumerable.Repeat("FF", width)), height).ToList()
    };

    public int Pixel(int x, int y)
    {
        if (IsRgba) throw new InvalidOperationException("Use RgbaPixel for RGBA artwork.");
        var index = Convert.ToInt32(Rows[y].Substring(x * 2, 2), 16);
        return index == 255 ? -1 : index;
    }

    public void SetPixel(int x, int y, int color)
    {
        if (IsRgba) throw new InvalidOperationException("Use SetRgbaPixel for RGBA artwork.");
        var row = Rows[y].ToCharArray();
        var pair = color < 0 ? "FF" : color.ToString("X2");
        row[x * 2] = pair[0]; row[x * 2 + 1] = pair[1];
        Rows[y] = new string(row);
    }

    public uint RgbaPixel(int x, int y) => Convert.ToUInt32(Rows[y].Substring(x * 8, 8), 16);
    public void SetRgbaPixel(int x, int y, uint color)
    {
        var row = Rows[y].ToCharArray();
        RgbaColor.Hex(color).AsSpan(1).CopyTo(row.AsSpan(x * 8, 8));
        Rows[y] = new string(row);
    }
}

public sealed class TextObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Key { get; set; } = "new.line";
    public double X { get; set; } = 16;
    public double Y { get; set; } = 16;
    public double Width { get; set; } = 200;
    public double Height { get; set; } = 56;
    public double FontSize { get; set; } = 16;
    public string FontId { get; set; } = "comic-shanns";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public int Color { get; set; } = 0;
    public Dictionary<string, TextPlacement> Placements { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<TextStyleSpan> Styles { get; set; } = [];

    public TextPlacement Placement(string language, string fallback) => language != fallback && Placements.TryGetValue(language, out var value)
        ? value : new TextPlacement { X = X, Y = Y, Width = Width, Height = Height };

    public void SetPlacement(string language, string fallback, TextPlacement value)
    {
        if (language == fallback) { X = value.X; Y = value.Y; Width = value.Width; Height = value.Height; }
        else Placements[language] = value;
    }

    public (string FontId, double Size) StyleAt(string language, string text, int index)
    {
        var styles = ExpandStyles(language, text);
        return styles.Length == 0 ? (FontId, FontSize) : styles[Math.Clamp(index, 0, styles.Length - 1)];
    }

    public void ChangeStyle(string language, string text, int start, int length, int sizeDelta = 0, string? fontId = null, double? fontSize = null)
    {
        var styles = ExpandStyles(language, text);
        var end = Math.Clamp((long)start + length, 0, styles.Length);
        for (var i = Math.Clamp(start, 0, styles.Length); i < end; i++)
            styles[i] = (fontId ?? styles[i].FontId, Math.Clamp(fontSize ?? styles[i].Size + sizeDelta, 1, 2048));
        CompressStyles(language, styles);
    }

    public void RetargetStyles(string language, string oldText, string newText)
    {
        if (oldText == newText) return;
        var oldStyles = ExpandStyles(language, oldText);
        var updated = Enumerable.Repeat((FontId, FontSize), newText.Length).ToArray();
        var prefix = 0;
        while (prefix < oldText.Length && prefix < newText.Length && oldText[prefix] == newText[prefix]) prefix++;
        var suffix = 0;
        while (suffix < oldText.Length - prefix && suffix < newText.Length - prefix && oldText[^(suffix + 1)] == newText[^(suffix + 1)]) suffix++;
        Array.Copy(oldStyles, 0, updated, 0, prefix);
        Array.Copy(oldStyles, oldText.Length - suffix, updated, newText.Length - suffix, suffix);
        if (newText.Length > prefix + suffix && oldStyles.Length > 0)
        {
            var inherited = oldStyles[Math.Clamp(prefix - 1, 0, oldStyles.Length - 1)];
            Array.Fill(updated, inherited, prefix, newText.Length - prefix - suffix);
        }
        CompressStyles(language, updated);
    }

    private (string FontId, double Size)[] ExpandStyles(string language, string text)
    {
        var result = Enumerable.Repeat((FontId, FontSize), text.Length).ToArray();
        foreach (var span in Styles.Where(s => string.Equals(s.Language, language, StringComparison.OrdinalIgnoreCase)))
            for (var i = Math.Clamp(span.Start, 0, text.Length); i < Math.Min(text.Length, (long)span.Start + span.Length); i++)
                result[i] = (span.FontId, span.FontSize);
        return result;
    }

    private void CompressStyles(string language, (string FontId, double Size)[] styles)
    {
        Styles.RemoveAll(s => string.Equals(s.Language, language, StringComparison.OrdinalIgnoreCase));
        for (var i = 0; i < styles.Length;)
        {
            var style = styles[i]; var end = i + 1;
            while (end < styles.Length && styles[end] == style) end++;
            if (style != (FontId, FontSize)) Styles.Add(new TextStyleSpan { Language = language, Start = i, Length = end - i, FontId = style.FontId, FontSize = style.Size });
            i = end;
        }
    }
}

public sealed class TextStyleSpan
{
    public string Language { get; set; } = "en";
    public int Start { get; set; }
    public int Length { get; set; }
    public string FontId { get; set; } = "comic-shanns";
    public double FontSize { get; set; } = 16;
}

public sealed class TextPlacement
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}
