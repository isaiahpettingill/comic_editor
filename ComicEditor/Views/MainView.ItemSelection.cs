using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ComicEditor.Editing;
using ComicEditor.Format;

namespace ComicEditor.Views;

public partial class MainView
{
    private readonly HashSet<string> selectedFrameIds = [], selectedLayerIds = [], selectedTextIds = [];
    private string? frameAnchor, layerAnchor, textAnchor, selectionFrameId, selectionPrimaryFrame, selectionPrimaryLayer, selectionPrimaryText;
    private Cutscene? itemSelectionScene;

    private void SyncItemSelection()
    {
        if (!ReferenceEquals(itemSelectionScene, editor.Scene))
        {
            itemSelectionScene = editor.Scene; selectedFrameIds.Clear(); selectedLayerIds.Clear(); selectedTextIds.Clear();
            frameAnchor = layerAnchor = textAnchor = selectionFrameId = selectionPrimaryFrame = selectionPrimaryLayer = selectionPrimaryText = null;
        }
        selectedFrameIds.IntersectWith(editor.Scene.Frames.Select(f => f.Id));
        if (selectionPrimaryFrame != editor.Frame.Id)
        {
            selectedFrameIds.Clear(); selectedFrameIds.Add(editor.Frame.Id);
            frameAnchor = selectionPrimaryFrame = editor.Frame.Id;
        }
        if (selectionFrameId != editor.Frame.Id)
        {
            selectionFrameId = editor.Frame.Id; selectedLayerIds.Clear(); selectedTextIds.Clear();
            selectedLayerIds.Add(editor.Layer.Id); layerAnchor = selectionPrimaryLayer = editor.Layer.Id; textAnchor = selectionPrimaryText = null;
        }
        if (selectionPrimaryLayer != editor.Layer.Id)
        {
            selectedLayerIds.Clear(); selectedLayerIds.Add(editor.Layer.Id); layerAnchor = selectionPrimaryLayer = editor.Layer.Id;
        }
        if (selectionPrimaryText != editor.SelectedTextId)
        {
            selectedTextIds.Clear();
            if (editor.SelectedTextId is { } selected) selectedTextIds.Add(selected);
            textAnchor = selectionPrimaryText = editor.SelectedTextId;
        }
        selectedLayerIds.IntersectWith(editor.Frame.Layers.Select(l => l.Id));
        selectedTextIds.IntersectWith(editor.Frame.TextObjects.Select(t => t.Id));
    }

    private static void SelectRange(HashSet<string> selected, string[] order, string id, KeyModifiers modifiers, ref string? anchor)
    {
        var toggle = modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Meta);
        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            if (!toggle) selected.Clear();
            var from = Array.IndexOf(order, anchor ?? id); var to = Array.IndexOf(order, id);
            if (from < 0) from = to;
            for (var i = Math.Min(from, to); i <= Math.Max(from, to); i++) if (i >= 0) selected.Add(order[i]);
            anchor ??= id;
        }
        else
        {
            if (!toggle) selected.Clear();
            if (!toggle || !selected.Remove(id)) selected.Add(id);
            anchor = id;
        }
    }

    private void SelectListItem(int index, bool frame, KeyModifiers modifiers, Action activate)
    {
        SyncItemSelection();
        if (frame)
        {
            var id = editor.Scene.Frames[index].Id;
            SelectRange(selectedFrameIds, editor.Scene.Frames.Select(f => f.Id).ToArray(), id, modifiers, ref frameAnchor);
            selectionPrimaryFrame = id;
        }
        else
        {
            var id = editor.Frame.Layers[index].Id; selectionPrimaryLayer = id;
            // Layers are displayed top-to-bottom in reverse storage order.
            SelectRange(selectedLayerIds, editor.Frame.Layers.AsEnumerable().Reverse().Select(l => l.Id).ToArray(), id, modifiers, ref layerAnchor);
        }
        activate(); RefreshAll();
        if (!frame) inspector?.GetVisualDescendants().OfType<Border>().FirstOrDefault(c => c.Name == $"LayerItem{index}")?.Focus();
    }

    private void SelectTextItem(TextObject text, KeyModifiers modifiers)
    {
        SyncItemSelection();
        SelectRange(selectedTextIds, editor.Frame.TextObjects.Select(t => t.Id).ToArray(), text.Id, modifiers, ref textAnchor);
        selectionPrimaryText = text.Id; editor.SelectedTextId = text.Id; editor.Tool = Tool.Text; RefreshTools(); RefreshInspector(); RefreshCanvas();
        inspector?.GetVisualDescendants().OfType<Button>().FirstOrDefault(c => c.Name == "TextSelect_" + text.Id)?.Focus();
    }

    private List<Frame> FrameTargets(Frame clicked)
    {
        SyncItemSelection();
        return selectedFrameIds.Contains(clicked.Id) ? editor.Scene.Frames.Where(f => selectedFrameIds.Contains(f.Id)).ToList() : [clicked];
    }

    private List<ArtworkLayer> LayerTargets(ArtworkLayer clicked)
    {
        SyncItemSelection();
        return selectedLayerIds.Contains(clicked.Id) ? editor.Frame.Layers.Where(l => selectedLayerIds.Contains(l.Id)).ToList() : [clicked];
    }

    private void DeleteFrames(Frame clicked)
    {
        var targets = FrameTargets(clicked); if (targets.Count >= editor.Scene.Frames.Count) return;
        FinishPath(); EndInlineTextEdit(); editor.BeforeChange();
        var current = editor.Frame; foreach (var frame in targets) editor.Scene.Frames.Remove(frame);
        editor.SelectFrame(Math.Max(0, editor.Scene.Frames.IndexOf(current)));
        selectedFrameIds.Clear(); selectionPrimaryFrame = null; RefreshAll();
    }

    private void DuplicateFrames(Frame clicked)
    {
        var targets = FrameTargets(clicked); if (targets.Count == 0) return;
        FinishPath(); EndInlineTextEdit(); editor.BeforeChange();
        var inserted = new List<Frame>();
        foreach (var source in targets)
        {
            var next = source.Snapshot(); next.Id = Guid.NewGuid().ToString("N");
            foreach (var layer in next.Layers) layer.Id = Guid.NewGuid().ToString("N");
            foreach (var text in next.TextObjects) text.Id = Guid.NewGuid().ToString("N");
            editor.Scene.Frames.Insert(editor.Scene.Frames.IndexOf(source) + 1, next); inserted.Add(next);
        }
        editor.SelectFrame(editor.Scene.Frames.IndexOf(inserted[0])); selectionPrimaryFrame = editor.Frame.Id;
        selectedFrameIds.Clear(); selectedFrameIds.UnionWith(inserted.Select(f => f.Id)); frameAnchor = inserted[0].Id; RefreshAll();
    }

    // Keyboard commands act on selection, not the separately focused/active item.
    // Keep the clicked-item fallbacks in FrameTargets/LayerTargets for context menus.
    private Frame? SelectedFrameTarget()
    {
        SyncItemSelection();
        return editor.Scene.Frames.FirstOrDefault(f => selectedFrameIds.Contains(f.Id));
    }

    private void DuplicateSelectedFrames()
    {
        if (SelectedFrameTarget() is { } target) DuplicateFrames(target);
    }

    private void DeleteSelectedFrames()
    {
        if (SelectedFrameTarget() is { } target) DeleteFrames(target);
    }

    private void MoveSelectedFrames(int delta)
    {
        if (SelectedFrameTarget() is { } target) MoveFrames(target, delta);
    }

    private void MoveFrames(Frame clicked, int delta)
    {
        var targets = FrameTargets(clicked).ToHashSet(); var frames = editor.Scene.Frames;
        if (targets.Count == 0 || (delta < 0 ? targets.Contains(frames[0]) : targets.Contains(frames[^1]))) return;
        editor.BeforeChange();
        if (delta < 0) for (var i = 1; i < frames.Count; i++) { if (targets.Contains(frames[i])) (frames[i - 1], frames[i]) = (frames[i], frames[i - 1]); }
        else for (var i = frames.Count - 2; i >= 0; i--) { if (targets.Contains(frames[i])) (frames[i + 1], frames[i]) = (frames[i], frames[i + 1]); }
        var primary = frames.FindIndex(f => f.Id == selectionPrimaryFrame); editor.SelectFrame(Math.Max(0, primary)); RefreshAll();
    }

    private void DeleteLayers(ArtworkLayer clicked)
    {
        var targets = LayerTargets(clicked); if (targets.Count >= editor.Frame.Layers.Count) return;
        FinishPath(); EndInlineTextEdit(); editor.BeforeChange(); var active = editor.Layer;
        foreach (var layer in targets) editor.Frame.Layers.Remove(layer);
        editor.LayerIndex = Math.Max(0, editor.Frame.Layers.IndexOf(active)); selectedLayerIds.Clear(); selectedLayerIds.Add(editor.Layer.Id); RefreshAll();
    }

    private void MoveLayers(ArtworkLayer clicked, int delta)
    {
        var targets = LayerTargets(clicked).ToHashSet(); var layers = editor.Frame.Layers;
        if (targets.Count == 0 || (delta < 0 ? targets.Contains(layers[0]) : targets.Contains(layers[^1]))) return;
        editor.BeforeChange(); var active = editor.Layer;
        if (delta < 0) for (var i = 1; i < layers.Count; i++) { if (targets.Contains(layers[i])) (layers[i - 1], layers[i]) = (layers[i], layers[i - 1]); }
        else for (var i = layers.Count - 2; i >= 0; i--) { if (targets.Contains(layers[i])) (layers[i + 1], layers[i]) = (layers[i], layers[i + 1]); }
        editor.LayerIndex = layers.IndexOf(active); RefreshAll();
    }

    private void DeleteTextObjects(TextObject clicked)
    {
        SyncItemSelection();
        var targets = selectedTextIds.Contains(clicked.Id) ? editor.Frame.TextObjects.Where(t => selectedTextIds.Contains(t.Id)).ToList() : [clicked];
        FinishPath(); EndInlineTextEdit(); editor.BeforeChange();
        foreach (var text in targets) editor.Frame.TextObjects.Remove(text);
        if (targets.Any(t => t.Id == editor.SelectedTextId)) editor.SelectedTextId = null;
        selectedTextIds.Clear(); RefreshAll();
    }

    private void ReorderSelectedItems(int from, int insertion, bool frames)
    {
        SyncItemSelection();
        if (frames)
        {
            var list = editor.Scene.Frames; var clicked = list[from]; var targets = FrameTargets(clicked).ToHashSet();
            var moved = list.Where(targets.Contains).ToList();
            var place = Math.Clamp(insertion, 0, list.Count);
            var before = list.Take(place).Count(targets.Contains);
            var remaining = list.Where(f => !targets.Contains(f)).ToList();
            remaining.InsertRange(place - before, moved);
            if (remaining.SequenceEqual(list)) return;
            editor.BeforeChange(); list.Clear(); list.AddRange(remaining);
            editor.SelectFrame(list.IndexOf(clicked)); selectionPrimaryFrame = clicked.Id;
            selectedFrameIds.Clear(); selectedFrameIds.UnionWith(moved.Select(f => f.Id));
        }
        else
        {
            var list = editor.Frame.Layers; var clicked = list[from]; var targets = LayerTargets(clicked).ToHashSet();
            var visual = list.AsEnumerable().Reverse().ToList(); var moved = visual.Where(targets.Contains).ToList();
            var place = Math.Clamp(insertion, 0, visual.Count); var before = visual.Take(place).Count(targets.Contains);
            var remaining = visual.Where(l => !targets.Contains(l)).ToList(); remaining.InsertRange(place - before, moved);
            remaining.Reverse(); if (remaining.SequenceEqual(list)) return;
            editor.BeforeChange(); list.Clear(); list.AddRange(remaining); editor.LayerIndex = list.IndexOf(clicked); selectionPrimaryLayer = clicked.Id;
            selectedLayerIds.Clear(); selectedLayerIds.UnionWith(moved.Select(l => l.Id));
            if (editor.Tool == Tool.Text) editor.Tool = Tool.Pixel;
        }
    }

    private bool CutFocusedFrames(object? source)
    {
        if (FocusedFrameIndex(source) is null) return false;
        var target = SelectedFrameTarget();
        if (target is null || FrameTargets(target).Count >= editor.Scene.Frames.Count) return true;
        CopyFrame(editor.Scene.Frames.IndexOf(target)); DeleteFrames(target); return true;
    }

    private bool NavigateFocusedItems(KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down or Key.Left or Key.Right) || e.KeyModifiers.HasFlag(KeyModifiers.Alt) || e.Source is not Avalonia.Visual visual) return false;
        var controls = new[] { visual }.Concat(visual.GetVisualAncestors()).OfType<Control>().ToArray();
        if (controls.Any(c => c is TextBox or NumericUpDown)) return false;
        var row = controls.FirstOrDefault(c => c.Name?.StartsWith("FrameRow", StringComparison.Ordinal) == true ||
            c.Name?.StartsWith("LayerRow", StringComparison.Ordinal) == true || c.Name?.StartsWith("TextRow_", StringComparison.Ordinal) == true);
        if (row?.Name is not string name) return false;
        var horizontalKey = e.Key is Key.Left or Key.Right;
        var horizontalStory = name.StartsWith("FrameRow") && storyboard?.Orientation == Avalonia.Layout.Orientation.Horizontal;
        if (horizontalKey && !horizontalStory) return false;
        var delta = e.Key is Key.Up or Key.Left ? -1 : 1;
        var focusOnly = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0 && !e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (name.StartsWith("FrameRow") && int.TryParse(name[8..], out var f))
        {
            var next = Math.Clamp(f + delta, 0, editor.Scene.Frames.Count - 1);
            if (!focusOnly) SelectListItem(next, true, e.KeyModifiers, () => SelectFrame(next));
            var card = storyboard?.Children.OfType<Border>().FirstOrDefault(c => c.Name == $"FrameRow{next}");
            card?.Focus(); card?.BringIntoView();
        }
        else if (name.StartsWith("LayerRow") && int.TryParse(name[8..], out var l))
        {
            var next = Math.Clamp(l - delta, 0, editor.Frame.Layers.Count - 1);
            if (!focusOnly) SelectListItem(next, false, e.KeyModifiers, () => { editor.LayerIndex = next; if (editor.Tool == Tool.Text) editor.Tool = Tool.Pixel; });
            else inspector?.GetVisualDescendants().OfType<Border>().FirstOrDefault(c => c.Name == $"LayerItem{next}")?.Focus();
        }
        else if (name.StartsWith("TextRow_"))
        {
            var i = editor.Frame.TextObjects.FindIndex(t => t.Id == name[8..]); if (i < 0) return false;
            var next = editor.Frame.TextObjects[Math.Clamp(i + delta, 0, editor.Frame.TextObjects.Count - 1)];
            if (!focusOnly) SelectTextItem(next, e.KeyModifiers);
            else inspector?.GetVisualDescendants().OfType<Button>().FirstOrDefault(c => c.Name == "TextSelect_" + next.Id)?.Focus();
        }
        else return false;
        return true;
    }

    private bool SelectAllFocusedItems(object? source)
    {
        if (source is not Avalonia.Visual visual) return false;
        var controls = new[] { visual }.Concat(visual.GetVisualAncestors()).OfType<Control>().ToArray();
        if (controls.Any(c => c is TextBox or NumericUpDown)) return false;
        SyncItemSelection();
        if (controls.Any(c => c.Name?.StartsWith("FrameRow", StringComparison.Ordinal) == true))
            selectedFrameIds.UnionWith(editor.Scene.Frames.Select(f => f.Id));
        else if (controls.Any(c => c.Name?.StartsWith("LayerRow", StringComparison.Ordinal) == true))
            selectedLayerIds.UnionWith(editor.Frame.Layers.Select(l => l.Id));
        else if (controls.Any(c => c.Name?.StartsWith("TextRow_", StringComparison.Ordinal) == true))
            selectedTextIds.UnionWith(editor.Frame.TextObjects.Select(t => t.Id));
        else return false;
        RefreshAll(); return true;
    }

    private bool DeleteFocusedItems(object? source)
    {
        if (source is not Avalonia.Visual visual) return false;
        var parents = new[] { visual }.Concat(visual.GetVisualAncestors()).OfType<Control>().ToArray();
        if (parents.Any(c => c is TextBox or NumericUpDown)) return false;
        var row = parents.FirstOrDefault(c => c.Name?.StartsWith("FrameRow", StringComparison.Ordinal) == true ||
            c.Name?.StartsWith("LayerRow", StringComparison.Ordinal) == true || c.Name?.StartsWith("TextRow_", StringComparison.Ordinal) == true);
        if (row?.Name is not string name) return false;
        SyncItemSelection();
        if (name.StartsWith("FrameRow") && int.TryParse(name[8..], out var f) && f >= 0 && f < editor.Scene.Frames.Count)
            DeleteSelectedFrames();
        else if (name.StartsWith("LayerRow") && int.TryParse(name[8..], out var l) && l >= 0 && l < editor.Frame.Layers.Count)
        {
            if (editor.Frame.Layers.FirstOrDefault(layer => selectedLayerIds.Contains(layer.Id)) is { } target) DeleteLayers(target);
        }
        else if (name.StartsWith("TextRow_") && editor.Frame.TextObjects.Any(t => t.Id == name[8..]))
        {
            if (editor.Frame.TextObjects.FirstOrDefault(text => selectedTextIds.Contains(text.Id)) is { } target) DeleteTextObjects(target);
        }
        else return false;
        return true;
    }
}
