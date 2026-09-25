using System;
using Avalonia;
using ComicEditor.Updating;

namespace ComicEditor.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--apply-update", var plan]) return UpdateInstaller.RunHelper(plan);
        UpdateHost.DesktopEnabled = true;
        if (args is ["--updated", var resume]) UpdateHost.ResumePlan = resume;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions { WmClass = "org.comiceditor.storyboard" })
            .WithInterFont()
            .LogToTrace();
}
