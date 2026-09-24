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
            CanvasHeight = (uint)scene.Height, FallbackLanguage = scene.FallbackLanguage
        };
        document.PaletteRgb.Add(scene.Palette.Select(hex => Convert.ToUInt32(hex[1..], 16)));
        foreach (var frame in scene.Frames)
        {
            var target = new Wire.Frame { Id = frame.Id, TextHidden = !frame.TextVisible };
            foreach (var layer in frame.Layers)
            {
                var pixels = layer.Rows.SelectMany(row => Enumerable.Range(0, scene.Width)
                    .Select(x => Convert.ToByte(row.Substring(x * 2, 2), 16))).ToArray();
                target.Layers.Add(new Wire.ArtworkLayer { Id = layer.Id, Name = layer.Name, Visible = layer.Visible, Pixels = ByteString.CopyFrom(pixels) });
            }
            foreach (var obj in frame.TextObjects)
                target.TextObjects.Add(new Wire.TextObject
                {
                    Id = obj.Id, Key = obj.Key, X = (float)obj.X, Y = (float)obj.Y,
                    Width = (float)obj.Width, Height = (float)obj.Height,
                    FontId = obj.FontId, FontSize = (float)obj.FontSize, PaletteIndex = (uint)obj.Color,
                    Bold = obj.Bold, Italic = obj.Italic
                });
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
        if (bytes.Length > 64 * 1024 * 1024) throw new InvalidDataException("Cutscene exceeds 64 MiB.");
        var document = Wire.CutsceneDocument.Parser.ParseFrom(bytes);
        if (document.CanvasWidth is < 1 or > 2048 || document.CanvasHeight is < 1 or > 2048)
            throw new InvalidDataException("Unsupported canvas dimensions.");
        var scene = new Cutscene
        {
            Version = 2, Width = (int)document.CanvasWidth, Height = (int)document.CanvasHeight,
            FallbackLanguage = document.FallbackLanguage,
            Palette = document.PaletteRgb.Select(color => $"#{color:X6}").ToList(),
            Frames = document.Frames.Select(frame => new Frame
            {
                Id = frame.Id, TextVisible = !frame.TextHidden,
                Layers = frame.Layers.Select(layer => new ArtworkLayer
                {
                    Id = layer.Id, Name = layer.Name, Visible = layer.Visible,
                    Rows = DecodeRows(layer.Pixels, (int)document.CanvasWidth, (int)document.CanvasHeight, document.Version == 1 ? 16 : 128)
                }).ToList(),
                TextObjects = frame.TextObjects.Select(obj => new TextObject
                {
                    Id = obj.Id, Key = obj.Key, X = obj.X, Y = obj.Y,
                    Width = obj.Width, Height = obj.Height, FontId = obj.FontId,
                    FontSize = obj.FontSize, Color = (int)obj.PaletteIndex,
                    Bold = obj.Bold, Italic = obj.Italic
                }).ToList()
            }).ToList(),
            Translations = document.Languages.ToDictionary(language => language.Code,
                language => language.Entries.ToDictionary(item => item.Key, item => item.Value), StringComparer.OrdinalIgnoreCase)
        };
        if (document.Version == 1 && scene.Palette.Count == 16)
            scene.Palette.AddRange(Cutscene.DefaultPalette().Skip(16));
        else if (document.Version != 2)
            throw new InvalidDataException("Unsupported cutscene version.");
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
}
