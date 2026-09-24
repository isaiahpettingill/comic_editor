using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace ComicEditor.Android;

[Activity(
    Label = "ComicEditor",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
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
