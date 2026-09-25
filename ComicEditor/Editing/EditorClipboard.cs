using ComicEditor.Format;

namespace ComicEditor.Editing;

/// <summary>Copies artwork colors by value so a destination project may have a different palette.</summary>
internal sealed class PaletteTransfer(IReadOnlyList<string> source, List<string> destination, bool destinationRgba)
{
    private readonly Dictionary<int, int> indices = [];
    private readonly Dictionary<uint, int> colors = [];
    public bool PaletteChanged { get; private set; }

    public uint SourceColor(int index) => RgbaColor.Parse(source[index]);

    public int MapIndex(int index)
    {
        if (indices.TryGetValue(index, out var mapped)) return mapped;
        mapped = MapColor(SourceColor(index)); indices[index] = mapped;
        return mapped;
    }

    public int MapColor(uint rgba)
    {
        if (colors.TryGetValue(rgba, out var mapped)) return mapped;
        var color = destinationRgba ? RgbaColor.Hex(rgba) : RgbaColor.Hex(RgbaColor.Blend(rgba, 0xffffffff))[..7];
        mapped = destination.FindIndex(value => string.Equals(value, color, StringComparison.OrdinalIgnoreCase));
        if (mapped < 0 && destination.Count < (destinationRgba ? 65535 : 255))
        {
            mapped = destination.Count; destination.Add(color); PaletteChanged = true;
        }
        if (mapped < 0)
        {
            var wanted = RgbaColor.Parse(color);
            var bestDistance = long.MaxValue;
            for (var i = 0; i < destination.Count; i++)
            {
                var candidate = RgbaColor.Parse(destination[i]);
                var dr = (int)(wanted >> 24) - (int)(candidate >> 24);
                var dg = (int)((wanted >> 16) & 255) - (int)((candidate >> 16) & 255);
                var db = (int)((wanted >> 8) & 255) - (int)((candidate >> 8) & 255);
                var da = destinationRgba ? (int)(wanted & 255) - (int)(candidate & 255) : 0;
                var distance = (long)dr * dr + dg * dg + db * db + da * da;
                if (distance >= bestDistance) continue;
                bestDistance = distance; mapped = i;
            }
        }
        colors[rgba] = mapped;
        return mapped;
    }
}

public sealed class ArtworkClipboard
{
    private readonly ArtworkSelection artwork;
    private readonly string[] palette;
    private readonly bool rgba;

    public ArtworkClipboard(ArtworkSelection selection, Cutscene scene)
    {
        artwork = selection.Copy(selection.Owner);
        palette = scene.Palette.ToArray(); rgba = scene.IsRgba;
    }

    public int X => artwork.X;
    public int Y => artwork.Y;
    public int Width => artwork.Width;
    public int Height => artwork.Height;

    public ArtworkSelection Paste(ArtworkLayer destination, Cutscene scene, int x, int y, out bool paletteChanged)
    {
        var placed = artwork.Copy(destination); placed.X = x; placed.Y = y;
        var transfer = new PaletteTransfer(palette, scene.Palette, scene.IsRgba);
        for (var i = 0; i < placed.Pixels.Length; i++)
        {
            if (!placed.Mask[i]) continue;
            var source = artwork.Pixels[i];
            if (rgba ? (source & 255) == 0 : source == 0) { placed.Pixels[i] = 0; continue; }
            var color = rgba ? source : transfer.SourceColor((int)source - 1);
            if (scene.IsRgba)
            {
                var px = x + i % placed.Width; var py = y + i / placed.Width;
                if (px >= 0 && py >= 0 && px < scene.Width && py < scene.Height)
                    color = RgbaColor.Blend(color, destination.RgbaPixel(px, py));
                placed.Pixels[i] = color;
            }
            else placed.Pixels[i] = (uint)(transfer.MapColor(color) + 1);
        }
        placed.Paste(); paletteChanged = transfer.PaletteChanged;
        return placed;
    }
}

public sealed class FrameClipboard
{
    private readonly Frame frame;
    private readonly int width, height;
    private readonly bool rgba;
    private readonly string[] palette;
    private readonly Dictionary<string, Dictionary<string, string>> translations;
    private readonly string[] fallbackFonts;

    public FrameClipboard(Cutscene scene, int index)
    {
        frame = scene.Frames[index].Snapshot(); width = scene.Width; height = scene.Height; rgba = scene.IsRgba;
        palette = scene.Palette.ToArray();
        translations = scene.Translations.ToDictionary(pair => pair.Key,
            pair => new Dictionary<string, string>(pair.Value, pair.Value.Comparer), scene.Translations.Comparer);
        fallbackFonts = scene.FallbackFontIds.ToArray();
    }

    public Frame Paste(Cutscene destination, out bool paletteChanged)
    {
        var copy = frame.Snapshot(); copy.Id = Guid.NewGuid().ToString("N");
        var transfer = new PaletteTransfer(palette, destination.Palette, destination.IsRgba);
        foreach (var layer in copy.Layers)
        {
            layer.Id = Guid.NewGuid().ToString("N");
            var rows = new List<string>(destination.Height);
            for (var y = 0; y < destination.Height; y++)
            {
                var output = new byte[destination.Width * (destination.IsRgba ? 4 : 1)];
                if (!destination.IsRgba) Array.Fill(output, (byte)255);
                if (y < height)
                {
                    var input = Convert.FromHexString(layer.Rows[y]);
                    for (var x = 0; x < Math.Min(width, destination.Width); x++)
                    {
                        var color = rgba
                            ? ((uint)input[x * 4] << 24) | ((uint)input[x * 4 + 1] << 16) | ((uint)input[x * 4 + 2] << 8) | input[x * 4 + 3]
                            : input[x] == 255 ? 0 : transfer.SourceColor(input[x]);
                        if ((color & 255) == 0) continue;
                        if (destination.IsRgba)
                        {
                            var offset = x * 4;
                            output[offset] = (byte)(color >> 24); output[offset + 1] = (byte)(color >> 16);
                            output[offset + 2] = (byte)(color >> 8); output[offset + 3] = (byte)color;
                        }
                        else output[x] = (byte)(rgba ? transfer.MapColor(color) : transfer.MapIndex(input[x]));
                    }
                }
                rows.Add(Convert.ToHexString(output));
            }
            layer.Rows = rows; layer.IsRgba = destination.IsRgba;
        }
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        var occupiedKeys = destination.Frames.SelectMany(f => f.TextObjects).Select(t => t.Key)
            .Concat(destination.Translations.Values.SelectMany(entries => entries.Keys)).ToHashSet(StringComparer.Ordinal);
        foreach (var text in copy.TextObjects)
        {
            text.Id = Guid.NewGuid().ToString("N");
            text.Color = transfer.MapIndex(text.Color);
            if (keys.TryGetValue(text.Key, out var key)) { text.Key = key; continue; }
            key = text.Key;
            if (occupiedKeys.Contains(key))
            {
                var number = 2;
                while (occupiedKeys.Contains($"{text.Key}.copy{number}")) number++;
                key = $"{text.Key}.copy{number}";
            }
            keys[text.Key] = key; occupiedKeys.Add(key);
            foreach (var (language, entries) in translations)
            {
                if (!entries.TryGetValue(text.Key, out var value)) continue;
                if (!destination.Translations.TryGetValue(language, out var target))
                    destination.Translations[language] = target = new Dictionary<string, string>(StringComparer.Ordinal);
                target[key] = value;
            }
            text.Key = key;
        }
        foreach (var font in fallbackFonts)
            if (destination.FallbackFontIds.Count < 128 && !destination.FallbackFontIds.Contains(font, StringComparer.OrdinalIgnoreCase))
                destination.FallbackFontIds.Add(font);
        paletteChanged = transfer.PaletteChanged;
        return copy;
    }
}
