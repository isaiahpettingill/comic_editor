using Avalonia.Media;
using Avalonia.Media.Fonts;
using ComicEditor.Fonts;
using ComicEditor.Format;
using System.Text;

namespace ComicEditor.Rendering;

// These stable IDs are part of the game rendering contract. Custom IDs use system:<family>.
public static class CutsceneFonts
{
    public static readonly string[] Ids = ["comic-shanns", "google:Anton", "google:Permanent Marker", "google:Noto Sans", "google:Noto Color Emoji"];
    public static readonly string[] Names = ["Comic Shanns", "Anton", "Permanent Marker", "Noto Sans", "Noto Color Emoji", "Custom system font…"];
    private const string Root = "avares://ComicEditor/Assets/Fonts#";
    private static readonly Dictionary<string, FontFamily> Cache = new(StringComparer.Ordinal);

    public static FontFamily Resolve(string? id, string language = "en")
    {
        EnsureManager();
        id = string.IsNullOrWhiteSpace(id) ? "comic-shanns" : id;
        var key = id + "|" + language;
        if (Cache.TryGetValue(key, out var cached)) return cached;
        var names = new[] { PrimaryName(id), Root + "Noto Sans", Root + "Noto Color Emoji" }
            .Concat(Imported.Where(p => p.Key.StartsWith("google:Noto "))
                .OrderBy(p => p.Key == "google:" + LanguageFonts.CjkFamily(language) ? 0 : 1)
                .ThenBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Value));
        var family = new FontFamily(string.Join(",", names.Distinct()));
        Cache[key] = family; return family;
    }

    private static string PrimaryName(string id) => Normalize(id) switch
        {
            "comic-shanns" => Root + "Comic Shanns",
            "anton" or "google:Anton" => Root + "Anton",
            "permanent-marker" or "google:Permanent Marker" => Root + "Permanent Marker",
            "noto-sans" or "google:Noto Sans" => Root + "Noto Sans",
            "google:Noto Color Emoji" => Root + "Noto Color Emoji",
            _ => id.StartsWith("google:", StringComparison.Ordinal) ? (Imported.TryGetValue(id, out var imported) ? imported : Root + "Noto Sans") : id.StartsWith("system:", StringComparison.Ordinal) ? id[7..] : id
        };

    public static string Normalize(string id) => string.IsNullOrWhiteSpace(id) ? "comic-shanns" : id switch { "anton" => "google:Anton", "permanent-marker" => "google:Permanent Marker", "noto-sans" => "google:Noto Sans", _ => id };
    public static bool IsCustom(string id) => Normalize(id) != "comic-shanns" && !id.StartsWith("google:") && id is not ("anton" or "permanent-marker" or "noto-sans");
    public static string NameFor(string id) => id == "comic-shanns" ? "Comic Shanns" : id.StartsWith("google:") ? id[7..] : id;
    private sealed class DownloadCollection : FontCollectionBase { public override Uri Key { get; } = new("fonts:ComicEditorDownloads"); }
    private static DownloadCollection? collection;
    private static FontManager? manager;
    private static readonly Dictionary<string, string> Imported = new();
    public static IEnumerable<string> ImportedIds { get { EnsureManager(); return Imported.Keys; } }

    private static void EnsureManager()
    {
        if (manager != FontManager.Current)
        {
            manager = FontManager.Current; collection = new DownloadCollection();
            manager.AddFontCollection(collection); Imported.Clear(); Cache.Clear();
        }
    }

    public static string Register(DownloadedFont font)
    {
        EnsureManager();
        var id = "google:" + font.Family;
        if (Imported.ContainsKey(id)) return id;
        using var stream = new MemoryStream(font.Data, writable: false);
        if (!collection!.TryAddGlyphTypeface(stream, out var face)) throw new InvalidDataException("The downloaded font could not be opened.");
        var familyName = string.IsNullOrWhiteSpace(face.TypographicFamilyName) ? face.FamilyName : face.TypographicFamilyName;
        Imported[id] = collection.Key + "#" + familyName; Cache.Clear();
        return id;
    }

    public static async Task EnsureAsync(Cutscene scene)
    {
        EnsureManager();
        foreach (var id in scene.Frames.SelectMany(f => f.TextObjects).Select(t => Normalize(t.FontId)).Distinct())
        {
            if (!id.StartsWith("google:") || Ids.Contains(id) || Imported.ContainsKey(id)) continue;
            await LoadGoogleAsync("https://fonts.google.com/specimen/" + Uri.EscapeDataString(id[7..]));
        }
        await LoadCachedFallbacksAsync(scene);
    }

    public static IReadOnlyList<MissingFont> Missing(string text, string fontId, string language)
    {
        EnsureManager();
        var result = new Dictionary<string, MissingFont>(StringComparer.Ordinal);
        var primary = new[] { PrimaryName(fontId), Root + "Noto Sans", Root + "Noto Color Emoji" };
        var faces = new Dictionary<string, GlyphTypeface?>(StringComparer.Ordinal);
        bool Covers(string family, Rune rune)
        {
            if (!faces.TryGetValue(family, out var face))
            { FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily(family)), out face); faces[family] = face; }
            return face?.CharacterToGlyphMap.ContainsGlyph(rune.Value) == true;
        }
        foreach (var rune in text.EnumerateRunes().Distinct())
        {
            if (!LanguageFonts.NeedsGlyph(rune) || primary.Any(f => Covers(f, rune))) continue;
            var family = LanguageFonts.FamilyFor(rune, language);
            if (family is not null && Imported.TryGetValue("google:" + family, out var loaded) && Covers(loaded, rune)) continue;
            var key = family ?? "";
            result.TryAdd(key, new MissingFont(key, family is null ? "Choose a font covering this character" : LanguageFonts.Description(family), rune.ToString()));
        }
        return result.Values.ToArray();
    }

    public static IReadOnlyList<MissingFont> Missing(Cutscene scene, string? onlyLanguage = null) =>
        scene.Frames.Where(f => f.TextVisible).SelectMany(f => f.TextObjects)
            .SelectMany(t => scene.Translations.Keys.Where(l => onlyLanguage is null || l == onlyLanguage)
                .Select(l => (Text: scene.RenderText(l, t.Key), t.FontId, Language: l))).Distinct()
            .SelectMany(t => Missing(t.Text, t.FontId, t.Language))
            .DistinctBy(f => f.Family).ToArray();

    public static async Task LoadCachedFallbacksAsync(Cutscene scene)
    {
        var ids = scene.FallbackFontIds.Concat(Missing(scene).Where(f => f.Family.Length > 0).Select(f => f.Id)).Distinct().ToArray();
        foreach (var id in ids)
            if (!Imported.ContainsKey(id) && await FontStorage.Read(id[7..]) is { } font) Register(font);
    }

    public static IEnumerable<string> UsedFallbackIds(Cutscene scene) => scene.Frames.SelectMany(f => f.TextObjects)
        .SelectMany(t => scene.Translations.Keys.SelectMany(l => scene.RenderText(l, t.Key).EnumerateRunes()
            .Select(r => LanguageFonts.FamilyFor(r, l))))
        .Where(f => f is not null && Imported.ContainsKey("google:" + f)).Select(f => "google:" + f).Distinct();

    public static void RequireAvailable(Cutscene scene, string? language = null)
    {
        var missing = Missing(scene, language);
        if (missing.Count > 0)
            throw new InvalidDataException("Missing fonts: " + string.Join(", ", missing.Select(f => f.Family.Length > 0 ? f.Family : $"U+{char.ConvertToUtf32(f.Sample, 0):X}")) +
                ". Install them in the editor before exporting, or use comic-compile --download-fonts to download supported language fonts.");
    }

    public static async Task<string> LoadGoogleAsync(string link, CancellationToken cancellationToken = default)
    {
        var family = GoogleFontDownload.FamilyFromLink(link); var id = "google:" + family;
        EnsureManager();
        if (Ids.Contains(id) || Imported.ContainsKey(id)) return id;
        if (await FontStorage.Read(family) is { } cached) return Register(cached);
        var font = await GoogleFontDownload.FetchAsync(link, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var registered = Register(font);
        await FontStorage.Write(font);
        return registered;
    }
    public static bool IsInstalled(string id)
    {
        var name = id.StartsWith("system:", StringComparison.Ordinal) ? id[7..] : id;
        return FontManager.Current.SystemFonts.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
