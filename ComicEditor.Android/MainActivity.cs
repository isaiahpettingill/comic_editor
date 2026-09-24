using Android.App;
using Android.Content.PM;
using Android.Content;
using Avalonia;
using Avalonia.Android;

namespace ComicEditor.Android;

[Activity(
    Label = "ComicEditor",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault], DataMimeType = "application/vnd.comiceditor.cutscene")]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault], DataSchemes = ["file", "content"], DataHost = "*", DataPathPatterns = [".*\\.ctsc", ".*\\.cutscene"], DataMimeType = "*/*")]
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnPause()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
        {
            if (ComicEditor.Editing.SessionStorage.Flush is { } flush) await flush();
        });
        base.OnPause();
    }
}
