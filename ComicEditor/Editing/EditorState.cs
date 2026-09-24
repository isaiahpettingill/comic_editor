using ComicEditor.Format;

namespace ComicEditor.Editing;

public enum Tool { Pixel, Smooth, Pressure, Eraser, Fill, Line, Rectangle, Ellipse, Eyedropper, Text }

public sealed class EditorState
{
    private readonly Stack<(byte[] Data, int Frame)> undo = new();
    private readonly Stack<(byte[] Data, int Frame)> redo = new();
    private byte[]? saved;
    public bool IsDirty => saved is not null && !CutsceneFile.Write(Scene).AsSpan().SequenceEqual(saved);
    public void MarkSaved() => saved = CutsceneFile.Write(Scene);
    public EditorState() => MarkSaved();
    public Cutscene Scene { get; private set; } = Cutscene.Create();
    public int FrameIndex { get; private set; }
    public int LayerIndex { get; set; }
    public Tool Tool { get; set; } = Tool.Pixel;
    public int Color { get; set; }
    public int BrushSize { get; set; } = 1;
    public string Language { get; set; } = "en";
    public bool OnionSkin { get; set; }
    public double OnionOpacity { get; set; } = 0.35;
    public bool Compare { get; set; }
    public string? SelectedTextId { get; set; }
    public string? FileName { get; set; }
    public Frame Frame => Scene.Frames[FrameIndex];
    public ArtworkLayer Layer => Frame.Layers[Math.Clamp(LayerIndex, 0, Frame.Layers.Count - 1)];
    public TextObject? SelectedText => Frame.TextObjects.FirstOrDefault(t => t.Id == SelectedTextId);

    public void BeforeChange()
    {
        undo.Push((CutsceneFile.Write(Scene), FrameIndex));
        if (undo.Count > 60)
        {
            var latest = undo.ToArray().Take(60).Reverse().ToArray();
            undo.Clear(); foreach (var value in latest) undo.Push(value);
        }
        redo.Clear();
    }

    public bool Undo() => Restore(undo, redo);
    public bool Redo() => Restore(redo, undo);

    private bool Restore(Stack<(byte[] Data, int Frame)> source, Stack<(byte[] Data, int Frame)> destination)
    {
        if (!source.TryPop(out var state)) return false;
        destination.Push((CutsceneFile.Write(Scene), FrameIndex));
        Scene = CutsceneFile.Parse(state.Data);
        FrameIndex = state.Frame;
        LayerIndex = Math.Min(LayerIndex, Frame.Layers.Count - 1);
        SelectedTextId = null;
        EnsureLanguage();
        return true;
    }

    public void Load(byte[] data, string? name = null)
    {
        Scene = CutsceneFile.Parse(data);
        FrameIndex = 0; LayerIndex = 0; SelectedTextId = null; FileName = name;
        undo.Clear(); redo.Clear();
        EnsureLanguage(); MarkSaved();
    }

    public void EnsureLanguage()
    {
        if (!Scene.Translations.ContainsKey(Language)) Language = Scene.Translations.Keys.FirstOrDefault() ?? "";
    }

    public string NewTextKey()
    {
        var keys = Scene.Frames.SelectMany(f => f.TextObjects).Select(t => t.Key).ToHashSet();
        var n = 1;
        while (keys.Contains($"line.{n}")) n++;
        return $"line.{n}";
    }

    public void SelectFrame(int index)
    {
        FrameIndex = Math.Clamp(index, 0, Scene.Frames.Count - 1);
        LayerIndex = Math.Min(LayerIndex, Frame.Layers.Count - 1);
        SelectedTextId = null;
    }

    public void AddFrame(bool duplicate)
    {
        BeforeChange();
        var next = duplicate ? CutsceneFile.Parse(CutsceneFile.Write(Scene)).Frames[FrameIndex] : Frame.Create(Scene.Width, Scene.Height);
        next.Id = Guid.NewGuid().ToString("N");
        foreach (var layer in next.Layers) layer.Id = Guid.NewGuid().ToString("N");
        foreach (var text in next.TextObjects) text.Id = Guid.NewGuid().ToString("N");
        Scene.Frames.Insert(++FrameIndex, next);
        SelectedTextId = null;
    }

    public void DeleteFrame()
    {
        if (Scene.Frames.Count == 1) return;
        BeforeChange(); Scene.Frames.RemoveAt(FrameIndex);
        SelectFrame(Math.Min(FrameIndex, Scene.Frames.Count - 1));
    }

    public void MoveFrame(int delta)
    {
        var target = FrameIndex + delta;
        if (target < 0 || target >= Scene.Frames.Count) return;
        BeforeChange();
        var frame = Frame;
        Scene.Frames.RemoveAt(FrameIndex); Scene.Frames.Insert(target, frame);
        FrameIndex = target;
    }
}
