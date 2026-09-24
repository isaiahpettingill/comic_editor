using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using Android.Content;
using Android.OS;
using Android.Provider;
using ComicEditor.Updating;

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
    protected override void OnCreate(Bundle? savedInstanceState)
    {
#if !DEBUG
        if (System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64)
        {
            UpdateHost.AndroidInstallation = new UpdateInstallation(ReleaseClient.AppVersion, "android-arm64");
            UpdateHost.AndroidDownloadDirectory = Path.Combine(CacheDir!.AbsolutePath, "updates");
            UpdateHost.InstallAndroid = InstallUpdate;
        }
#endif
        base.OnCreate(savedInstanceState);
    }

    private bool InstallUpdate(string package)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && !PackageManager!.CanRequestPackageInstalls())
        {
            StartActivity(new Intent(Settings.ActionManageUnknownAppSources, global::Android.Net.Uri.Parse("package:" + PackageName)));
            return false;
        }
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(this, PackageName + ".updates", new Java.IO.File(package));
        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(uri, "application/vnd.android.package-archive");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission);
        StartActivity(intent); return true;
    }
}
