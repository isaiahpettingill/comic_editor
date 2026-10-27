using System.Buffers;
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
            // Wire v5 changes RGBA source storage only; the editable model remains v4.
            Version = scene.IsRgba ? 5u : (uint)scene.Version,
            CanvasWidth = (uint)scene.Width,
            CanvasHeight = (uint)scene.Height,
            FallbackLanguage = scene.FallbackLanguage
        };
        if (scene.IsRgba) document.PaletteRgba.Add(scene.Palette.Select(RgbaColor.Parse));
        else document.PaletteRgb.Add(scene.Palette.Select(hex => Convert.ToUInt32(hex[1..], 16)));
        document.FallbackFontIds.Add(scene.FallbackFontIds);
        foreach (var frame in scene.Frames)
        {
            var target = new Wire.Frame { Id = frame.Id, AudioQoa = ByteString.CopyFrom(frame.AudioQoa), TextHidden = !frame.TextVisible, DurationMs = (uint)frame.DurationMs, Req = frame.Requirement };
            foreach (var layer in frame.Layers)
            {
                if (scene.IsRgba)
                {
                    var count = scene.Width * scene.Height;
                    var rgba = ArrayPool<uint>.Shared.Rent(count);
                    try
                    {
                        for (var y = 0; y < scene.Height; y++)
                            layer.ReadRgbaRow(y, rgba.AsSpan(y * scene.Width, scene.Width));
                        target.Layers.Add(new Wire.ArtworkLayer { Id = layer.Id, Name = layer.Name, Visible = layer.Visible, RgbaQoi = ByteString.CopyFrom(RgbaQoi.Encode(scene.Width, scene.Height, rgba.AsSpan(0, count))) });
                    }
                    finally { ArrayPool<uint>.Shared.Return(rgba); }
                    continue;
                }
                var length = scene.Width * scene.Height;
                var pixels = ArrayPool<byte>.Shared.Rent(length);
                try
                {
                    for (var y = 0; y < scene.Height; y++)
                        layer.ReadIndexedRow(y, pixels.AsSpan(y * scene.Width, scene.Width));
                    target.Layers.Add(new Wire.ArtworkLayer { Id = layer.Id, Name = layer.Name, Visible = layer.Visible, Pixels = ByteString.CopyFrom(pixels, 0, length) });
                }
                finally { ArrayPool<byte>.Shared.Return(pixels); }
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
                    Italic = obj.Italic,
                    RotationDegrees = (float)obj.RotationDegrees,
                    SizeEffect = (uint)obj.SizeEffect,
                    CurveDegrees = (float)obj.CurveDegrees,
                    CurveAnchorX = (float)obj.CurveAnchorX,
                    CurveAnchorY = (float)obj.CurveAnchorY
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
        if (document.Version is < 1 or > 5 ||
            (document.Version == 1 && document.PaletteRgb.Count != 16) ||
            (document.Version == 2 && document.PaletteRgb.Count != 128) ||
            (document.Version >= 4 ? document.PaletteRgba.Count is < 2 or > 65535 || document.PaletteRgb.Count != 0 : document.PaletteRgb.Count is < 2 or > 255 || document.PaletteRgba.Count != 0))
            throw new InvalidDataException("Unsupported cutscene version or palette size.");
        if (document.CanvasWidth is < 1 or > 2048 || document.CanvasHeight is < 1 or > 2048)
            throw new InvalidDataException("Unsupported canvas dimensions.");
        var scene = new Cutscene
        {
            Version = document.Version >= 4 ? 4 : 3,
            Width = (int)document.CanvasWidth,
            Height = (int)document.CanvasHeight,
            FallbackLanguage = document.FallbackLanguage,
            FallbackFontIds = document.FallbackFontIds.ToList(),
            Palette = document.Version >= 4 ? document.PaletteRgba.Select(RgbaColor.Hex).ToList() : document.PaletteRgb.Select(color => $"#{color:X6}").ToList(),
            Frames = document.Frames.Select(frame => new Frame
            {
                Id = frame.Id,
                TextVisible = !frame.TextHidden,
                DurationMs = frame.DurationMs == 0 ? FramePlayback.LegacyDurationMs : checked((int)frame.DurationMs),
                AudioQoa = frame.AudioQoa.ToByteArray(),
                Requirement = frame.Req.Length == 0 ? "always" : frame.Req,
                Layers = frame.Layers.Select(layer => DecodeLayer(layer, document)).ToList(),
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
                    RotationDegrees = obj.RotationDegrees,
                    SizeEffect = (TextSizeEffect)obj.SizeEffect,
                    CurveDegrees = obj.CurveDegrees,
                    CurveAnchorX = obj.CurveAnchorX,
                    CurveAnchorY = obj.CurveAnchorY,
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

    private static ArtworkLayer DecodeLayer(Wire.ArtworkLayer source, Wire.CutsceneDocument document)
    {
        var width = (int)document.CanvasWidth; var height = (int)document.CanvasHeight;
        var layer = ArtworkLayer.Create(source.Name, width, height, document.Version >= 4);
        layer.Id = source.Id; layer.Visible = source.Visible;
        if (layer.IsRgba)
        {
            if (!source.Pixels.IsEmpty || (document.Version == 4
                ? source.RgbaPng.IsEmpty || !source.RgbaQoi.IsEmpty
                : source.RgbaQoi.IsEmpty || !source.RgbaPng.IsEmpty))
                throw new InvalidDataException($"RGBA layer payload does not match cutscene version {document.Version}.");
            var pixels = document.Version == 4
                ? RgbaPng.Decode(source.RgbaPng.Span, width, height)
                : RgbaQoi.Decode(source.RgbaQoi.Span, width, height);
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++) layer.SetRgbaPixel(x, y, pixels[y * width + x]);
        }
        else
        {
            if (!source.RgbaPng.IsEmpty || !source.RgbaQoi.IsEmpty)
                throw new InvalidDataException("Indexed layer must not contain RGBA image payloads.");
            var bytes = source.Pixels.Span;
            if (bytes.Length != width * height) throw new InvalidDataException("Indexed layer has an invalid pixel count.");
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var index = bytes[y * width + x];
                    if (index != 255 && index >= document.PaletteRgb.Count) throw new InvalidDataException("Indexed pixel exceeds the palette.");
                    layer.SetPixel(x, y, index == 255 ? -1 : index);
                }
        }
        return layer;
    }
}
