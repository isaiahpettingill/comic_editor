using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using ComicEditor.Editing;

namespace ComicEditor.Views;

public partial class MainView
{
    private sealed class ProjectTab(EditorState editor)
    {
        public EditorState Editor { get; } = editor;
        public ProjectFile? File { get; set; }
        public string? Bookmark { get; set; }
        public bool AutoSavePaused { get; set; }
        public DateTimeOffset LastAutoSave { get; set; } = DateTimeOffset.UtcNow;
        public bool Dirty { get; set; }
    }

    private readonly List<ProjectTab> tabs = [];
    private ProjectTab activeTab = null!;
    private StackPanel? tabStrip;

    private void SaveActiveTab()
    {
        activeTab.File = currentFile;
        activeTab.Bookmark = currentBookmark;
        activeTab.AutoSavePaused = autoSavePaused;
        activeTab.LastAutoSave = lastAutoSave;
        activeTab.Dirty = editor.FastDirty;
    }

    private void AddTab(EditorState state)
    {
        TrackEditor(state);
        var tab = new ProjectTab(state);
        tabs.Add(tab); SwitchTab(tab, force: true);
    }

    private void SwitchTab(ProjectTab tab, bool force = false)
    {
        if (fileBusy && !force || tab == activeTab) return;
        EndInlineTextEdit(); FinishPath(); StopSpray(); ResetCanvasNavigation();
        SaveActiveTab(); activeTab = tab; editor = tab.Editor;
        editor.FinishPendingEdit = () => FinishPath();
        currentFile = tab.File; currentBookmark = tab.Bookmark;
        autoSavePaused = tab.AutoSavePaused; lastAutoSave = tab.LastAutoSave;
        selection = clipboardSelection = null;
        Build(compact); RefreshTabs(); _ = SaveSessionSafely();
    }

    private void CloseTab(ProjectTab tab)
    {
        if (fileBusy) return;
        if (tab.Editor.IsDirty)
        {
            ShowModal("Close cutscene?", Label($"{tab.Editor.FileName ?? "Untitled"} has unsaved changes."),
                () => { CloseModal(); RemoveTab(tab); }, "Close without saving");
        }
        else RemoveTab(tab);
    }

    private void RemoveTab(ProjectTab tab)
    {
        var index = tabs.IndexOf(tab); if (index < 0) return;
        if (tab == activeTab)
        {
            EndInlineTextEdit(); FinishPath(); StopSpray(); ResetCanvasNavigation();
            if (tabs.Count == 1)
            {
                editor.New(); ClearFile(); Build(compact); _ = SaveSessionSafely(); return;
            }
            tabs.RemoveAt(index);
            activeTab = tabs[Math.Clamp(index, 0, tabs.Count - 1)]; editor = activeTab.Editor;
            editor.FinishPendingEdit = () => FinishPath();
            currentFile = activeTab.File; currentBookmark = activeTab.Bookmark;
            autoSavePaused = activeTab.AutoSavePaused; lastAutoSave = activeTab.LastAutoSave;
            selection = clipboardSelection = null; Build(compact);
        }
        else tabs.Remove(tab);
        RefreshTabs(); _ = SaveSessionSafely();
    }

    private void RefreshTabs()
    {
        if (tabStrip is null) return;
        tabStrip.Children.Clear();
        for (var i = 0; i < tabs.Count; i++)
        {
            var tab = tabs[i]; var index = i;
            var current = tab == activeTab;
            var dirty = current ? editor.FastDirty : tab.Dirty;
            var label = (tab.Editor.FileName ?? $"Untitled {i + 1}") + (dirty ? " ●" : "");
            var item = Button(label, () => SwitchTab(tab)); item.Name = $"ProjectTab{i}";
            item.Background = Avalonia.Media.Brushes.Transparent;
            item.BorderThickness = new Thickness(0);
            item.MinWidth = 80; item.MaxWidth = 165; item.Height = touchLayout ? 40 : 29;
            item.HorizontalContentAlignment = HorizontalAlignment.Left;
            var close = Button("×", () => CloseTab(tab)); close.Name = $"CloseProjectTab{i}";
            close.Width = close.Height = touchLayout ? 40 : 25;
            close.Padding = new Thickness(0); close.Background = Avalonia.Media.Brushes.Transparent;
            close.BorderThickness = new Thickness(0);
            var tabContent = new StackPanel { Orientation = Orientation.Horizontal, Children = { item, close } };
            tabStrip.Children.Add(new Border
            {
                Background = Brush(current ? UiTheme.Selection : UiTheme.Surface),
                BorderBrush = Brush(current ? UiTheme.Accent : UiTheme.Border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4, 4, 0, 0),
                Child = tabContent
            });
        }
        var add = Button("+", New); add.Name = "NewProjectTab"; add.Width = add.Height = touchLayout ? 40 : 29;
        tabStrip.Children.Add(add);
    }
}
