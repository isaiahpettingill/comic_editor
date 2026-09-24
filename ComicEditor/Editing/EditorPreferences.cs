using System.Text.Json;
using System.Text.Json.Serialization;
using ComicEditor.Format;

namespace ComicEditor.Editing;

public sealed class EditorPreferences
{
    public int CanvasWidth { get; set; } = 320;
    public int CanvasHeight { get; set; } = 180;
    public string FontId { get; set; } = "comic-shanns";
    public double FontSize { get; set; } = 16;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool SmoothMouse { get; set; }
    public int Color { get; set; }
    public Tool Tool { get; set; }
    public Dictionary<string, PaintSettings> Tools { get; set; } = new();
    public string[]? Palette { get; set; }
    public string Language { get; set; } = "en";
    public bool OnionSkin { get; set; }
    public double OnionOpacity { get; set; } = .35;
    public bool Compare { get; set; }
    public double Zoom { get; set; }
    public bool CheckForUpdates { get; set; } = true;
    public bool AutoSave { get; set; }
    public int AutoSaveMinutes { get; set; } = 2;

    // Hosts provide storage; plain instances (including tests) remain in memory.
    [JsonIgnore] public Action<string>? Persist { get; set; }
    public void Save() => Persist?.Invoke(JsonSerializer.Serialize(this, PreferencesJson.Default.EditorPreferences));

    public static EditorPreferences Parse(string? json)
    {
        EditorPreferences value;
        try { value = json is null ? new() : JsonSerializer.Deserialize(json, PreferencesJson.Default.EditorPreferences) ?? new(); }
        catch (JsonException) { value = new(); }
        value.CanvasWidth = Math.Clamp(value.CanvasWidth, 1, 2048);
        value.AutoSaveMinutes = Math.Clamp(value.AutoSaveMinutes, 1, 60);
        value.CanvasHeight = Math.Clamp(value.CanvasHeight, 1, 2048);
        value.FontSize = double.IsFinite(value.FontSize) ? Math.Clamp(value.FontSize, 1, 2048) : 16;
        if (string.IsNullOrWhiteSpace(value.FontId)) value.FontId = "comic-shanns";
        if (!Enum.IsDefined(value.Tool)) value.Tool = Tool.Pixel;
        value.Tools ??= new();
        value.Tools = value.Tools.Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value);
        foreach (var settings in value.Tools.Values) settings.Validate();
        if (value.Palette is not { Length: >= 2 and <= 255 } || value.Palette.Any(c => !GplPalette.IsHex(c))) value.Palette = null;
        value.Color = Math.Clamp(value.Color, 0, (value.Palette?.Length ?? 128) - 1);
        value.OnionOpacity = double.IsFinite(value.OnionOpacity) ? Math.Clamp(value.OnionOpacity, 0, 1) : .35;
        value.Zoom = double.IsFinite(value.Zoom) ? Math.Clamp(value.Zoom, 0, 32) : 0;
        return value;
    }

    public void Remember(TextObject text)
    {
        FontId = text.FontId; FontSize = text.FontSize; Bold = text.Bold; Italic = text.Italic; Save();
    }
}

public sealed class PaintSettings
{
    public int Size { get; set; } = 1;
    public BrushTip Tip { get; set; }
    public ShapeFill Fill { get; set; }
    public int SprayDensity { get; set; } = 12;
    public void Validate()
    {
        Size = Math.Clamp(Size, 1, 64); SprayDensity = Math.Clamp(SprayDensity, 1, 100);
        if (!Enum.IsDefined(Tip)) Tip = BrushTip.Round;
        if (!Enum.IsDefined(Fill)) Fill = ShapeFill.Outline;
    }
}

[JsonSerializable(typeof(EditorPreferences))]
internal partial class PreferencesJson : JsonSerializerContext { }

public static class PreferencesStorage
{
    public static Func<string?> Read { get; set; } = () => File.Exists(FilePath) ? File.ReadAllText(FilePath) : null;
    public static Action<string> Write { get; set; } = json =>
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, json); File.Move(temporary, FilePath, overwrite: true);
    };
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComicEditor", "preferences.json");
    public static EditorPreferences Load()
    {
        EditorPreferences result;
        try { result = EditorPreferences.Parse(Read()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { result = new(); }
        result.Persist = json =>
        {
            try { Write(json); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { System.Diagnostics.Debug.WriteLine(ex); }
        };
        return result;
    }
}
