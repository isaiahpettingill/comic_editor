using ComicEditor.Format;

namespace ComicEditor.Editing;

public enum Tool { Pixel, Smooth, Pressure, Eraser, Fill, Line, Rectangle, Ellipse, Eyedropper, Text, Spray, Select, Lasso, Curve, Polygon, RoundedRectangle, Zoom }

public sealed class EditorState
{
    private readonly Stack<(byte[] Data, int Frame)> undo = new();
    private readonly Stack<(byte[] Data, int Frame)> redo = new();
    private byte[]? saved;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public bool IsDirty => saved is not null && !CutsceneFile.Write(Scene).AsSpan().SequenceEqual(saved);
    public void MarkSaved(byte[]? written = null) => saved = written ?? CutsceneFile.Write(Scene);
    public void MarkUnsaved() => saved = [];
    public EditorState(EditorPreferences? preferences = null)
    {
        Preferences = preferences ?? new();
        Scene = CreateScene(); MarkSaved();
    }
    public EditorPreferences Preferences { get; }
    public Action? FinishPendingEdit { get; set; }
    public Cutscene Scene { get; private set; }
    public TextObject CreateText() => new()
    {
        Key = NewTextKey(),
        FontId = Preferences.FontId,
        FontSize = Preferences.FontSize,
        Bold = Preferences.Bold,
        Italic = Preferences.Italic,
        Color = Color
    };
    public void RememberCanvas()
    {
        Preferences.CanvasWidth = Scene.Width; Preferences.CanvasHeight = Scene.Height; Preferences.Save();
    }
    private Cutscene CreateScene()
    {
        var scene = Cutscene.Create(Preferences.CanvasWidth, Preferences.CanvasHeight);
        if (Preferences.Palette is not null) scene.Palette = Preferences.Palette.ToList();
        return scene;
    }
    public void New() => Load(CutsceneFile.Write(CreateScene()));
    public PaintSettings Paint => Preferences.Tools.TryGetValue(Tool.ToString(), out var settings) ? settings :
        Preferences.Tools[Tool.ToString()] = new PaintSettings { Size = Tool == Tool.Spray ? 16 : 1 };
    public int FrameIndex { get; private set; }
    private int layerIndex;
    public int LayerIndex { get => layerIndex; set { if (layerIndex != value) FinishPendingEdit?.Invoke(); layerIndex = value; } }
    public Tool Tool { get => Preferences.Tool; set { if (Preferences.Tool != value) FinishPendingEdit?.Invoke(); Preferences.Tool = value; Preferences.Save(); } }
    public int Color { get => Math.Clamp(Preferences.Color, 0, Scene.Palette.Count - 1); set { Preferences.Color = Math.Clamp(value, 0, Scene.Palette.Count - 1); Preferences.Save(); } }
    public int BrushSize { get => Paint.Size; set { Paint.Size = value; Preferences.Save(); } }
    public string Language { get => Preferences.Language; set { Preferences.Language = value; Preferences.Save(); } }
    public bool OnionSkin { get => Preferences.OnionSkin; set { Preferences.OnionSkin = value; Preferences.Save(); } }
    public double OnionOpacity { get => Preferences.OnionOpacity; set { Preferences.OnionOpacity = value; Preferences.Save(); } }
    public bool Compare { get => Preferences.Compare; set { Preferences.Compare = value; Preferences.Save(); } }
    public string? SelectedTextId { get; set; }
    public string? FileName { get; set; }
    public Frame Frame => Scene.Frames[FrameIndex];
    public ArtworkLayer Layer => Frame.Layers[Math.Clamp(LayerIndex, 0, Frame.Layers.Count - 1)];
    public TextObject? SelectedText => Frame.TextObjects.FirstOrDefault(t => t.Id == SelectedTextId);

    public void BeforeChange()
    {
        FinishPendingEdit?.Invoke();
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
        FinishPendingEdit?.Invoke();
        Scene = CutsceneFile.Parse(data);
        FrameIndex = 0; LayerIndex = 0; SelectedTextId = null; FileName = name;
        undo.Clear(); redo.Clear();
        EnsureLanguage(); MarkSaved();
        RememberCanvas();
        RememberPalette();
    }

    public void RememberPalette() { Preferences.Palette = Scene.Palette.ToArray(); Preferences.Save(); }

    public void ApplyPalette(IReadOnlyList<string> colors)
    {
        if (colors.Count is < 2 or > GplPalette.Capacity || colors.Any(c => !GplPalette.IsHex(c))) throw new ArgumentException("Expected 2–255 RGB colors.");
        var copy = colors.Select(c => c.ToUpperInvariant()).ToList();
        if (Scene.Palette.SequenceEqual(copy)) return;
        BeforeChange();
        // Indices still present keep their identity. Removed slots map to the
        // nearest remaining RGB color, across every layer and text object.
        var map = Enumerable.Range(0, Scene.Palette.Count).Select(i => i < copy.Count ? i : Nearest(Scene.Palette[i], copy)).ToArray();
        if (copy.Count < Scene.Palette.Count)
            foreach (var frame in Scene.Frames)
            {
                foreach (var layer in frame.Layers)
                    for (var y = 0; y < Scene.Height; y++)
                    {
                        var row = layer.Rows[y].ToCharArray();
                        for (var x = 0; x < Scene.Width; x++)
                        {
                            var index = layer.Pixel(x, y);
                            if (index < copy.Count) continue;
                            var hex = map[index].ToString("X2"); row[x * 2] = hex[0]; row[x * 2 + 1] = hex[1];
                        }
                        layer.Rows[y] = new string(row);
                    }
                foreach (var obj in frame.TextObjects) obj.Color = map[obj.Color];
            }
        var selected = map[Color]; Scene.Palette = copy; Color = selected; RememberPalette();
    }

    private static int Nearest(string source, IReadOnlyList<string> colors)
    {
        var rgb = Convert.FromHexString(source[1..]);
        return Enumerable.Range(0, colors.Count).MinBy(i =>
        {
            var other = Convert.FromHexString(colors[i][1..]);
            return Enumerable.Range(0, 3).Sum(c => (rgb[c] - other[c]) * (rgb[c] - other[c]));
        });
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
        FinishPendingEdit?.Invoke();
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
