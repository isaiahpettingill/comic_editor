using Avalonia.Media;
using Avalonia.Media.Fonts;
using ComicEditor.Fonts;
using ComicEditor.Format;
using System.Security.Cryptography;
using System.Text;

namespace ComicEditor.Rendering;

// These stable IDs are part of the game rendering contract. Custom IDs use system:<family>.
public static class CutsceneFonts
{
    public static readonly string[] Ids = ["comic-shanns", "google:Anton", "google:Permanent Marker", "google:Noto Sans"];
    public static readonly string[] Names = ["Comic Shanns", "Anton", "Permanent Marker", "Noto Sans", "Custom system font…"];
    private const string Root = "avares://ComicEditor/Assets/Fonts#";
    private const string Fallbacks = "," + Root + "Noto Sans," + Root + "Noto Sans Arabic," + Root + "Noto Sans Hebrew," +
        Root + "Noto Sans Devanagari," + Root + "Noto Sans Thai," + Root + "Noto Sans CJK SC";
    private static readonly Dictionary<string, FontFamily> Cache = new(StringComparer.Ordinal);

    public static FontFamily Resolve(string? id)
    {
        id = string.IsNullOrWhiteSpace(id) ? "comic-shanns" : id;
        if (Cache.TryGetValue(id, out var cached)) return cached;
        var name = id switch
        {
            "comic-shanns" => Root + "Comic Shanns",
            "anton" or "google:Anton" => Root + "Anton",
            "permanent-marker" or "google:Permanent Marker" => Root + "Permanent Marker",
            "noto-sans" or "google:Noto Sans" => Root + "Noto Sans",
            _ => id.StartsWith("google:", StringComparison.Ordinal) ? (Imported.TryGetValue(id, out var imported) ? imported : Root + "Noto Sans") : id.StartsWith("system:", StringComparison.Ordinal) ? id[7..] : id
        };
        var family = new FontFamily(name + Fallbacks);
        Cache[id] = family; return family;
    }

    public static string Normalize(string id) => string.IsNullOrWhiteSpace(id) ? "comic-shanns" : id switch { "anton" => "google:Anton", "permanent-marker" => "google:Permanent Marker", "noto-sans" => "google:Noto Sans", _ => id };
    public static bool IsCustom(string id) => Normalize(id) != "comic-shanns" && !id.StartsWith("google:") && id is not ("anton" or "permanent-marker" or "noto-sans");
    public static string NameFor(string id) => id == "comic-shanns" ? "Comic Shanns" : id.StartsWith("google:") ? id[7..] : id;
    private sealed class DownloadCollection : FontCollectionBase { public override Uri Key { get; } = new("fonts:ComicEditorDownloads"); }
    private static DownloadCollection? collection;
    private static FontManager? manager;
    private static readonly Dictionary<string, string> Imported = new();
    public static IEnumerable<string> ImportedIds => Imported.Keys;

    public static string Register(DownloadedFont font)
    {
        if (manager != FontManager.Current)
        {
            manager = FontManager.Current; collection = new DownloadCollection();
            manager.AddFontCollection(collection); Imported.Clear(); Cache.Clear();
        }
        var id = "google:" + font.Family;
        if (Imported.ContainsKey(id)) return id;
        using var stream = new MemoryStream(font.Data, writable: false);
        if (!collection!.TryAddGlyphTypeface(stream, out var face)) throw new InvalidDataException("The downloaded font could not be opened.");
        Imported[id] = collection.Key + "#" + face.FamilyName; Cache.Remove(id);
        return id;
    }

    public static async Task EnsureAsync(Cutscene scene)
    {
        foreach (var id in scene.Frames.SelectMany(f => f.TextObjects).Select(t => Normalize(t.FontId)).Distinct())
        {
            if (!id.StartsWith("google:") || Ids.Contains(id) || Imported.ContainsKey(id)) continue;
            await LoadGoogleAsync("https://fonts.google.com/specimen/" + Uri.EscapeDataString(id[7..]));
        }
    }

    public static async Task<string> LoadGoogleAsync(string link)
    {
        var family = GoogleFontDownload.FamilyFromLink(link); var id = "google:" + family;
        if (Ids.Contains(id) || Imported.ContainsKey(id)) return id;
        var cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComicEditor", "fonts");
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(family)));
        var cacheFile = Path.Combine(cacheRoot, key + ".font");
        DownloadedFont font;
        if (!OperatingSystem.IsBrowser() && File.Exists(cacheFile))
            font = new DownloadedFont(family, "cached.font", await File.ReadAllBytesAsync(cacheFile), "", link);
        else
        {
            font = await GoogleFontDownload.FetchAsync(link);
            if (!OperatingSystem.IsBrowser())
            {
                Directory.CreateDirectory(cacheRoot); await File.WriteAllBytesAsync(cacheFile, font.Data);
                await File.WriteAllTextAsync(Path.Combine(cacheRoot, key + ".license.txt"), font.License);
            }
        }
        return Register(font);
    }
    public static bool IsInstalled(string id)
    {
        var name = id.StartsWith("system:", StringComparison.Ordinal) ? id[7..] : id;
        return FontManager.Current.SystemFonts.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
