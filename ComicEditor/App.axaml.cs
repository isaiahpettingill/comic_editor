using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ComicEditor.Editing;
using ComicEditor.Views;

namespace ComicEditor;

public partial class App : Application
{
    private readonly List<IStorageFile> activatedFiles = [];
    private MainView? editorView;
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
            activatable.Activated += (_, args) =>
            {
                if (args is not FileActivatedEventArgs files) return;
                foreach (var file in files.Files.OfType<IStorageFile>().Where(file => ProjectTypes.IsProject(file.Name)))
                    if (editorView is { } view) Dispatcher.UIThread.Post(() => view.QueueOpenFile(file));
                    else activatedFiles.Add(file);
            };
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            editorView = (MainView)desktop.MainWindow.Content!;
            foreach (var path in desktop.Args ?? []) editorView.QueueOpenPath(path);
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory = () =>
            {
                editorView = new MainView();
                foreach (var file in activatedFiles) editorView.QueueOpenFile(file);
                activatedFiles.Clear();
                return editorView;
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = editorView = new MainView();
        }

        if (editorView is not null)
        {
            foreach (var file in activatedFiles) editorView.QueueOpenFile(file);
            activatedFiles.Clear();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
