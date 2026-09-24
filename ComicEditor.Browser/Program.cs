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

    private static Task Main(string[] args)
    {
        PreferencesStorage.Read = LoadPreferences;
        PreferencesStorage.Write = SavePreferences;
        return BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
