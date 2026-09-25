using Google.Protobuf;
using Wire = ComicEditor.Wire;

namespace ComicEditor.Format;

/// <summary>Reads and writes the public cutscene.proto wire format.</summary>
public static class CutsceneFile
{
    public static byte[] Write(Cutscene scene)
    {
        scene.Validate();
        var document = new Wire.CutsceneDocument
        {
            Version = (uint)scene.Version,
            CanvasWidth = (uint)scene.Width,
            CanvasHeight = (uint)scene.Height,
            FallbackLanguage = scene.FallbackLanguage
        };
        if (scene.IsRgba) document.PaletteRgba.Add(scene.Palette.Select(RgbaColor.Parse));
        else document.PaletteRgb.Add(scene.Palette.Select(hex => Convert.ToUInt32(hex[1..], 16)));
        document.FallbackFontIds.Add(scene.FallbackFontIds);
        foreach (var frame in scene.Frames)
        {
            var target = new Wire.Frame { Id = frame.Id, TextHidden = !frame.TextVisible };
            foreach (var layer in frame.Layers)
            {
                if (scene.IsRgba)
                {
                    var rgba = new uint[scene.Width * scene.Height];
                    for (var y = 0; y < scene.Height; y++)
                        for (var x = 0; x < scene.Width; x++) rgba[y * scene.Width + x] = layer.RgbaPixel(x, y);
                    target.Layers.Add(new Wire.ArtworkLayer { Id = layer.Id, Name = layer.Name, Visible = layer.Visible, RgbaPng = ByteString.CopyFrom(RgbaPng.Encode(scene.Width, scene.Height, rgba)) });
                    continue;
                }
                var pixels = new byte[scene.Width * scene.Height];
                for (var y = 0; y < scene.Height; y++)
                    Convert.FromHexString(layer.Rows[y].AsSpan(), pixels.AsSpan(y * scene.Width, scene.Width), out _, out _);
                target.Layers.Add(new Wire.ArtworkLayer { Id = layer.Id, Name = layer.Name, Visible = layer.Visible, Pixels = ByteString.CopyFrom(pixels) });
            }
            foreach (var obj in frame.TextObjects)
            {
                var text = new Wire.TextObject
                {
                    Id = obj.Id,
                    Key = obj.Key,
                    X = (float)obj.X,
                    Y = (float)obj.Y,
                    Width = (float)obj.Width,
                    Height = (float)obj.Height,
                    FontId = obj.FontId,
                    FontSize = (float)obj.FontSize,
                    PaletteIndex = (uint)obj.Color,
                    Bold = obj.Bold,
                    Italic = obj.Italic
                };
                foreach (var (language, placement) in obj.Placements.OrderBy(p => p.Key, StringComparer.Ordinal))
                    text.Placements.Add(new Wire.TextPlacementOverride
                    {
                        Language = language,
                        X = (float)placement.X,
                        Y = (float)placement.Y,
                        Width = (float)placement.Width,
                        Height = (float)placement.Height
                    });
                foreach (var style in obj.Styles)
                    text.Styles.Add(new Wire.TextStyleSpan
                    {
                        Language = style.Language,
                        Start = (uint)style.Start,
                        Length = (uint)style.Length,
                        FontId = style.FontId,
                        FontSize = (float)style.FontSize
                    });
                target.TextObjects.Add(text);
            }
            document.Frames.Add(target);
        }
        foreach (var language in scene.Translations.OrderBy(entry => entry.Key))
        {
            var target = new Wire.Language { Code = language.Key };
            foreach (var translation in language.Value.OrderBy(entry => entry.Key))
                target.Entries.Add(new Wire.Translation { Key = translation.Key, Value = translation.Value });
            document.Languages.Add(target);
        }
        return document.ToByteArray();
    }

    public static Cutscene Parse(byte[] bytes)
    {
        if (bytes.Length > 512 * 1024 * 1024) throw new InvalidDataException("Cutscene exceeds 512 MiB.");
        var document = Wire.CutsceneDocument.Parser.ParseFrom(bytes);
        if (document.Version is < 1 or > 4 ||
            (document.Version == 1 && document.PaletteRgb.Count != 16) ||
            (document.Version == 2 && document.PaletteRgb.Count != 128) ||
            (document.Version == 4 ? document.PaletteRgba.Count is < 2 or > 65535 || document.PaletteRgb.Count != 0 : document.PaletteRgb.Count is < 2 or > 255 || document.PaletteRgba.Count != 0))
            throw new InvalidDataException("Unsupported cutscene version or palette size.");
        if (document.CanvasWidth is < 1 or > 2048 || document.CanvasHeight is < 1 or > 2048)
            throw new InvalidDataException("Unsupported canvas dimensions.");
        var scene = new Cutscene
        {
            Version = document.Version == 4 ? 4 : 3,
            Width = (int)document.CanvasWidth,
            Height = (int)document.CanvasHeight,
            FallbackLanguage = document.FallbackLanguage,
            FallbackFontIds = document.FallbackFontIds.ToList(),
            Palette = document.Version == 4 ? document.PaletteRgba.Select(RgbaColor.Hex).ToList() : document.PaletteRgb.Select(color => $"#{color:X6}").ToList(),
            Frames = document.Frames.Select(frame => new Frame
            {
                Id = frame.Id,
                TextVisible = !frame.TextHidden,
                Layers = frame.Layers.Select(layer => new ArtworkLayer
                {
                    Id = layer.Id,
                    Name = layer.Name,
                    Visible = layer.Visible,
                    IsRgba = document.Version == 4,
                    Rows = document.Version == 4 ? DecodeRgbaRows(layer.RgbaPng, (int)document.CanvasWidth, (int)document.CanvasHeight) : DecodeRows(layer.Pixels, (int)document.CanvasWidth, (int)document.CanvasHeight, document.PaletteRgb.Count)
                }).ToList(),
                TextObjects = frame.TextObjects.Select(obj => new TextObject
                {
                    Id = obj.Id,
                    Key = obj.Key,
                    X = obj.X,
                    Y = obj.Y,
                    Width = obj.Width,
                    Height = obj.Height,
                    FontId = obj.FontId,
                    FontSize = obj.FontSize,
                    Color = (int)obj.PaletteIndex,
                    Bold = obj.Bold,
                    Italic = obj.Italic,
                    Placements = obj.Placements.ToDictionary(p => p.Language, p => new TextPlacement { X = p.X, Y = p.Y, Width = p.Width, Height = p.Height }, StringComparer.OrdinalIgnoreCase),
                    Styles = obj.Styles.Select(s => new TextStyleSpan { Language = s.Language, Start = (int)s.Start, Length = (int)s.Length, FontId = s.FontId, FontSize = s.FontSize }).ToList()
                }).ToList()
            }).ToList(),
            Translations = document.Languages.ToDictionary(language => language.Code,
                language => language.Entries.ToDictionary(item => item.Key, item => item.Value), StringComparer.OrdinalIgnoreCase)
        };
        if (document.Version == 1 && scene.Palette.Count == 16)
            scene.Palette.AddRange(Cutscene.DefaultPalette().Skip(16));
        if (!scene.Translations.ContainsKey(scene.FallbackLanguage))
            scene.FallbackLanguage = scene.Translations.ContainsKey("en") ? "en" : scene.Translations.Keys.FirstOrDefault() ?? "";
        scene.Validate();
        return scene;
    }

    private static List<string> DecodeRows(ByteString bytes, int width, int height, int paletteSize)
    {
        if (bytes.Length != width * height) throw new InvalidDataException("Indexed layer has an invalid pixel count.");
        var rows = new List<string>(height);
        for (var y = 0; y < height; y++)
        {
            var row = new char[width * 2];
            for (var x = 0; x < width; x++)
            {
                var index = bytes[y * width + x];
                if (index != 255 && index >= paletteSize) throw new InvalidDataException("Indexed pixel exceeds the palette.");
                row[x * 2] = "0123456789ABCDEF"[index >> 4];
                row[x * 2 + 1] = "0123456789ABCDEF"[index & 15];
            }
            rows.Add(new string(row));
        }
        return rows;
    }

    private static List<string> DecodeRgbaRows(ByteString bytes, int width, int height)
    {
        var pixels = RgbaPng.Decode(bytes.Span, width, height);
        var rows = new List<string>(height);
        for (var y = 0; y < height; y++)
        {
            var row = new char[width * 8];
            for (var x = 0; x < width; x++) RgbaColor.Hex(pixels[y * width + x]).AsSpan(1).CopyTo(row.AsSpan(x * 8, 8));
            rows.Add(new string(row));
        }
        return rows;
    }
}
