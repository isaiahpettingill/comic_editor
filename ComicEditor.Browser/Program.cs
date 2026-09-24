using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using ComicEditor;
using ComicEditor.Editing;
using ComicEditor.Fonts;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed partial class Program
{
    [JSImport("globalThis.comicEditorPreferences.load")]
    private static partial string? LoadPreferences();
    [JSImport("globalThis.comicEditorPreferences.save")]
    private static partial void SavePreferences(string json);

    [JSImport("globalThis.comicEditorSession.load")]
    private static partial Task<string?> LoadSession();
    [JSImport("globalThis.comicEditorSession.save")]
    private static partial Task SaveSession(string json);
    [JSImport("globalThis.comicEditorSession.onBackground")]
    private static partial void OnBackground([JSMarshalAs<JSType.Function>] Action callback);

    [JSImport("globalThis.comicEditorPalettes.listJson")]
    private static partial Task<string> ListPalettes();
    [JSImport("globalThis.comicEditorPalettes.read")]
    private static partial Task<string> ReadPalette(string name);
    [JSImport("globalThis.comicEditorPalettes.write")]
    private static partial Task WritePalette(string name, string text);

    [JSImport("globalThis.comicEditorFonts.read")]
    private static partial Task<string?> ReadFont(string key);
    [JSImport("globalThis.comicEditorFonts.write")]
    private static partial Task WriteFont(string key, string json);

    private static Task Main(string[] args)
    {
        PreferencesStorage.Read = LoadPreferences;
        PreferencesStorage.Write = SavePreferences;
        SessionStorage.Read = LoadSession;
        SessionStorage.Write = SaveSession;
        PaletteLibrary.List = async () => JsonSerializer.Deserialize(await ListPalettes(), PaletteListJson.Default.StringArray) ?? [];
        PaletteLibrary.Read = ReadPalette; PaletteLibrary.Write = WritePalette;
        FontStorage.BrowserRead = ReadFont; FontStorage.BrowserWrite = WriteFont;
        OnBackground(async () => { if (SessionStorage.Flush is { } flush) await flush(); });
        return BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}

[JsonSerializable(typeof(string[]))]
internal partial class PaletteListJson : JsonSerializerContext { }
