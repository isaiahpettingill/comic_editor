using Avalonia.Controls;
using Avalonia.Headless;
using ComicEditor.Format;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Fact]
    public async Task SidebarTypingAfterSaveIsDirtyAndNextSaveContainsLatestTextWithinOneUndo()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(async () =>
        {
            var view = new MainView(); var state = State(view);
            var window = new Window { Content = view, Width = 1280, Height = 800 }; window.Show();
            var path = Path.Combine(Path.GetTempPath(), "comic-translation-save-" + Guid.NewGuid().ToString("N") + ".ctsc");
            try
            {
                var obj = state.CreateText(); state.Frame.TextObjects.Add(obj); state.SelectedTextId = obj.Id;
                state.Scene.Translations[state.Language][obj.Key] = "Before";
                var original = CutsceneFile.Write(state.Scene); state.MarkSaved(original);
                await File.WriteAllBytesAsync(path, original);
                var file = await window.StorageProvider.TryGetFileFromPathAsync(new Uri(path)); Assert.NotNull(file);
                await (Task)Invoke(view, "BindFile", file!, original)!;
                Invoke(view, "RefreshAll"); _ = Capture(window);
                var box = Named<TextBox>(window, "TranslationText"); box.Focus();
                box.Text = "A"; _ = Capture(window);
                await (Task)Invoke(view, "Save")!;
                Assert.False(state.FastDirty);
                Assert.True(box.IsFocused);
                box.Text = "AB"; _ = Capture(window);
                Assert.True(state.FastDirty);
                await (Task)Invoke(view, "Save")!;
                Assert.Equal("AB", CutsceneFile.Parse(await File.ReadAllBytesAsync(path)).Text(state.Language, obj.Key));
                Assert.False(state.FastDirty);
                Assert.True(state.Undo());
                Assert.Equal("Before", state.Scene.Text(state.Language, obj.Key));
                Assert.False(state.CanUndo);
            }
            finally { window.Close(); if (File.Exists(path)) File.Delete(path); }
        }, CancellationToken.None);
    }
}
