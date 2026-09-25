using Avalonia.Controls;
using Avalonia.Headless;
using ComicEditor.Editing;
using ComicEditor.Format;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Theory]
    [InlineData(".ctsc", false)]
    [InlineData(".cutscene", false)]
    [InlineData(".CTSC", true)]
    public async Task AssociatedFileOpensInNewTabAndPreservesUnsavedEdits(string extension, bool dirty)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(async () =>
        {
            var path = Path.Combine(Path.GetTempPath(), "comic 中文 project " + Guid.NewGuid().ToString("N") + extension);
            var scene = Cutscene.Create(64, 48);
            await File.WriteAllBytesAsync(path, CutsceneFile.Write(scene));
            var view = new MainView(); var window = new Window { Content = view, Width = 1000, Height = 800 }; window.Show();
            try
            {
                var original = State(view);
                if (dirty) { original.BeforeChange(); original.Scene.Palette[0] = "#123456"; }
                view.QueueOpenPath(new Uri(path).AbsoluteUri);
                await (Task)Invoke(view, "OpenPendingFiles")!;
                if (dirty)
                {
                    Assert.Equal(64, State(view).Scene.Width);
                    Assert.Equal("#123456", original.Scene.Palette[0]);
                    Assert.True(original.IsDirty);
                }
                else
                {
                    Assert.Equal(64, State(view).Scene.Width); Assert.Equal(48, State(view).Scene.Height);
                    Assert.Equal(Path.GetFileName(path), State(view).FileName);
                    Assert.False(State(view).IsDirty);
                }
            }
            finally { _ = Capture(window); window.Close(); File.Delete(path); }
        }, CancellationToken.None);
    }
}
