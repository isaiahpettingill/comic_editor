using System.Runtime.Versioning;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using ComicEditor;
using ComicEditor.Editing;
using System.Runtime.InteropServices.JavaScript;

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

    private static Task Main(string[] args)
    {
        PreferencesStorage.Read = LoadPreferences;
        PreferencesStorage.Write = SavePreferences;
        SessionStorage.Read = LoadSession;
        SessionStorage.Write = SaveSession;
        OnBackground(async () => { if (SessionStorage.Flush is { } flush) await flush(); });
        return BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
