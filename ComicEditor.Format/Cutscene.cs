namespace ComicEditor.Format;

// Version 3 supports 2–255 colors. Hex rows still use FF for transparency.
// Protobuf stores the same index as one byte per pixel on disk.
public sealed class Cutscene
{
    public int Version { get; set; } = 3;
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

    public static Cutscene Create(int width = 320, int height = 180)
    {
        var scene = new Cutscene { Width = width, Height = height };
        scene.Frames.Add(Frame.Create(width, height));
        return scene;
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
                    var row = new string('F', width * 2).ToCharArray();
                    var sourceY = y - dy;
                    if (sourceY >= 0 && sourceY < Height && count > 0)
                        layer.Rows[sourceY].CopyTo(sourceX * 2, row, destX * 2, count * 2);
                    rows.Add(new string(row));
                }
                layer.Rows = rows;
            }
            foreach (var text in frame.TextObjects) { text.X += dx; text.Y += dy; }
        }
        Width = width; Height = height;
    }

    public void Validate()
    {
        if (Version != 3 || Width is < 1 or > 2048 || Height is < 1 or > 2048 || Palette.Count is < 2 or > 255 || Frames.Count == 0)
            throw new InvalidDataException("Unsupported cutscene dimensions, version, palette, or empty storyboard.");
        if (Palette.Any(p => p.Length != 7 || p[0] != '#' || !p[1..].All(Uri.IsHexDigit)))
            throw new InvalidDataException("Palette entries must be #RRGGBB colors.");
        if (FallbackFontIds.Count > 128 || FallbackFontIds.Any(id => !id.StartsWith("google:", StringComparison.Ordinal) || id.Length is < 8 or > 107 || !id[7..].All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-')))
            throw new InvalidDataException("Fallback fonts must be Google Fonts family references.");
        foreach (var frame in Frames)
        {
            if (frame.Layers.Count == 0) throw new InvalidDataException("Every frame needs an artwork layer.");
            foreach (var layer in frame.Layers)
            {
                if (layer.Rows.Count != Height || layer.Rows.Any(r => r.Length != Width * 2 ||
                    Enumerable.Range(0, Width).Any(x => !byte.TryParse(r.AsSpan(x * 2, 2), System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out var index) || (index != 255 && index >= Palette.Count))))
                    throw new InvalidDataException("Artwork must contain one valid palette byte per pixel.");
            }
            foreach (var obj in frame.TextObjects)
                if (string.IsNullOrWhiteSpace(obj.Key) || obj.Width <= 0 || obj.Height <= 0 || obj.FontSize <= 0 ||
                    !double.IsFinite(obj.X) || !double.IsFinite(obj.Y) || !double.IsFinite(obj.Width) ||
                    !double.IsFinite(obj.Height) || !double.IsFinite(obj.FontSize) || obj.Color < 0 || obj.Color >= Palette.Count)
                    throw new InvalidDataException("Invalid text object.");
        }
    }
}

public sealed class Frame
{
    public bool TextVisible { get; set; } = true;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public List<ArtworkLayer> Layers { get; set; } = [];
    public List<TextObject> TextObjects { get; set; } = [];

    public static Frame Create(int width, int height) => new()
    {
        Layers = [ArtworkLayer.Create("Artwork", width, height)]
    };
}

public sealed class ArtworkLayer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Artwork";
    public bool Visible { get; set; } = true;
    public List<string> Rows { get; set; } = [];

    public static ArtworkLayer Create(string name, int width, int height) => new()
    {
        Name = name,
        Rows = Enumerable.Repeat(string.Concat(Enumerable.Repeat("FF", width)), height).ToList()
    };

    public int Pixel(int x, int y)
    {
        var index = Convert.ToInt32(Rows[y].Substring(x * 2, 2), 16);
        return index == 255 ? -1 : index;
    }

    public void SetPixel(int x, int y, int color)
    {
        var row = Rows[y].ToCharArray();
        var pair = color < 0 ? "FF" : color.ToString("X2");
        row[x * 2] = pair[0]; row[x * 2 + 1] = pair[1];
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
}
