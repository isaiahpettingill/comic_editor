using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ComicEditor.Fonts;

public static class FontStorage
{
    // Browser hosts supply IndexedDB adapters. Desktop/Android keep the same
    // app-data cache used by earlier Google Font imports.
    public static Func<string, Task<string?>>? BrowserRead { get; set; }
    public static Func<string, string, Task>? BrowserWrite { get; set; }
    public static string DirectoryPath => Environment.GetEnvironmentVariable("COMIC_EDITOR_FONT_CACHE") is { Length: > 0 } custom
        ? Path.GetFullPath(custom) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ComicEditor", "fonts");
    private static string Key(string family) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(family)));

    public static async Task<DownloadedFont?> Read(string family)
    {
        if (BrowserRead is { } read)
        {
            var json = await read(Key(family));
            return json is null ? null : JsonSerializer.Deserialize(json, FontJson.Default.DownloadedFont);
        }
        if (OperatingSystem.IsBrowser()) return null;
        var path = Path.Combine(DirectoryPath, Key(family) + ".font");
        if (!File.Exists(path)) return null;
        return new DownloadedFont(family, "cached.font", await File.ReadAllBytesAsync(path), "", "cached");
    }

    public static async Task Write(DownloadedFont font)
    {
        if (BrowserWrite is { } write)
        {
            await write(Key(font.Family), JsonSerializer.Serialize(font, FontJson.Default.DownloadedFont));
            return;
        }
        if (OperatingSystem.IsBrowser()) return;
        Directory.CreateDirectory(DirectoryPath);
        var path = Path.Combine(DirectoryPath, Key(font.Family));
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, font.Data);
            await File.WriteAllTextAsync(path + ".license.txt", font.License);
            File.Move(temporary, path + ".font", overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

[JsonSerializable(typeof(DownloadedFont))]
internal partial class FontJson : JsonSerializerContext { }
