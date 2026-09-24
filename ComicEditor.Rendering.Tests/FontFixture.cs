using ComicEditor.Fonts;
using ComicEditor.Rendering;
using System.Security.Cryptography;
using System.Text;

namespace ComicEditor.Rendering.Tests;

internal static class FontFixture
{
    public static DownloadedFont Chinese(string family = "Noto Sans SC") => new(family, "fixture.ttf",
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets/NotoSansSC-Fixture.ttf")), "OFL", "offline test fixture");

    public static void Register(string family = "Noto Sans SC") => CutsceneFonts.Register(Chinese(family));

    public static void SeedCache(string directory)
    {
        Directory.CreateDirectory(directory);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("Noto Sans SC")));
        File.WriteAllBytes(Path.Combine(directory, key + ".font"), Chinese().Data);
    }
}
