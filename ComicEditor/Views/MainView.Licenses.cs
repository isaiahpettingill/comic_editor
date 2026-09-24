using Avalonia.Controls;
using ComicEditor.Updating;

namespace ComicEditor.Views;

public partial class MainView
{
    private void ShowLicenses()
    {
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(Label($"ComicEditor {ReleaseClient.AppVersion.ToString(3)} — 0BSD", true));
        body.Children.Add(Label("Color picker: AvaloniaColorPicker, copyright Giorgio Bianchini, licensed under LGPL-3.0-only. Includes an Avalonia 12 compatibility port. Source and rebuild instructions are included in the repository."));
        foreach (var (title, resource) in new[] { ("Read LGPL v3", "ComicEditor.LGPL"), ("Read GPL v3", "ComicEditor.GPL") })
            body.Children.Add(Button(title, () =>
            {
                using var stream = typeof(App).Assembly.GetManifestResourceStream(resource)!;
                using var reader = new StreamReader(stream);
                var text = reader.ReadToEnd(); CloseModal(); ShowModal(title, Label(text));
            }));
        body.Children.Add(Button("Source & third-party notices", async () =>
        {
            if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
                await launcher.LaunchUriAsync(new Uri("https://github.com/isaiahpettingill/comic_editor/tree/main/third_party/AvaloniaColorPicker"));
        }));
        ShowModal("About ComicEditor", body);
    }
}
