using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ComicEditor.Editing;
using ComicEditor.Format;
using ComicEditor.Rendering;
using IconPacks.Avalonia.Material;

namespace ComicEditor.Views;

public partial class MainView : UserControl
{
    private enum Pane { Storyboard, Canvas, Inspector }
    private readonly EditorState editor = new();
    private CutsceneCanvas? canvas, previous;
    private StackPanel? storyboard, inspector, palette, toolRail, toolOptions;
    private TextBlock? status, frameCount;
    private ComboBox? previewLanguage;
    private Grid? desktopWorkspace, rootGrid, shell, canvasPair;
    private Border? modal;
    private Control? returnFocus;
    private bool compact;
    private double paletteHeight = 150;
    private bool paletteResized;
    private readonly Pane[] paneOrder = [Pane.Storyboard, Pane.Canvas, Pane.Inspector];
    private readonly GridLength[] paneWidths = [new(190), new(1, GridUnitType.Star), new(300)];
    private Pane? draggingPane;
    private Point paneDragStart;
    private double zoom; // Zero fits the available canvas area.
    private ScrollViewer? canvasScroll;
    private Viewbox? canvasFit;

    public MainView()
    {
        InitializeComponent();
        Resources["SliderPreContentMargin"] = new GridLength(6);
        Resources["SliderPostContentMargin"] = new GridLength(6);
        Build(false);
        SizeChanged += (_, _) => { var small = Bounds.Width < 900; if (small != compact && modal is null) Build(small); };
        AttachedToVisualTree += (_, _) => RefreshTitle();
        KeyDown += OnKeyDown;
    }

    private static TextBlock Label(string text, bool bold = false) => new()
    {
        Text = text,
        FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap
    };

    private static Button Button(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            Height = 32,
            Padding = new Thickness(10, 4),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static Button Icon(PackIconMaterialKind kind, string tip, Action action)
    {
        var button = Button("", action);
        button.Content = new PackIconMaterial { Kind = kind, Width = 18, Height = 18 };
        button.Width = 32; button.Padding = new Thickness(5);
        ToolTip.SetTip(button, tip);
        Avalonia.Automation.AutomationProperties.SetName(button, tip);
        return button;
    }

    private static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        foreach (var child in children) row.Children.Add(child);
        return row;
    }

    private static void AddAt(Grid grid, Control child, int col = 0, int row = 0)
    { Grid.SetColumn(child, col); Grid.SetRow(child, row); grid.Children.Add(child); }

    private static MenuItem MenuGroup(string title, params (string Title, Action Action)[] entries)
    {
        var group = new MenuItem { Header = title };
        foreach (var (caption, action) in entries)
        {
            var parts = caption.Split('|');
            var item = new MenuItem { Header = parts[0] };
            if (parts.Length > 1) item.InputGesture = KeyGesture.Parse(parts[1]);
            item.Click += (_, _) => action(); group.Items.Add(item);
        }
        return group;
    }

    private Border PaneHeader(Pane pane, string caption)
    {
        var header = new Border
        {
            Name = pane + "Header",
            Background = Brush("#DBDEE3"),
            Padding = new Thickness(8, 5),
            Child = Row(new PackIconMaterial { Kind = PackIconMaterialKind.Drag, Width = 16, Height = 16 }, Label(caption, true)),
            Cursor = new Cursor(StandardCursorType.SizeAll)
        };
        ToolTip.SetTip(header, "Drag onto another pane to swap positions");
        header.PointerPressed += (_, e) =>
        {
            if (compact || desktopWorkspace is null) return;
            draggingPane = pane; paneDragStart = e.GetPosition(desktopWorkspace); e.Pointer.Capture(header);
            header.Background = Brush("#BDDDF2");
        };
        header.PointerReleased += (_, e) =>
        {
            var source = draggingPane; var workspace = desktopWorkspace;
            var x = workspace is null ? -1 : e.GetPosition(workspace).X;
            draggingPane = null; e.Pointer.Capture(null); header.Background = Brush("#DBDEE3");
            if (source is null || workspace is null || x < 0 || x > workspace.Bounds.Width || Math.Abs(x - paneDragStart.X) < 12) return;
            var edge = 0.0; var destination = 2;
            for (var i = 0; i < 3; i++)
            {
                edge += workspace.ColumnDefinitions[i * 2].ActualWidth;
                if (x < edge) { destination = i; break; }
                edge += 5;
            }
            var from = Array.IndexOf(paneOrder, source.Value);
            if (from == destination) return;
            (paneOrder[from], paneOrder[destination]) = (paneOrder[destination], paneOrder[from]); Build(false);
        };
        return header;
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));

    private void Build(bool small)
    {
        if (desktopWorkspace is not null && !compact)
            for (var i = 0; i < 3; i++) paneWidths[i] = desktopWorkspace.ColumnDefinitions[i * 2].Width;
        if (rootGrid is not null && !compact && rootGrid.RowDefinitions[4].ActualHeight > 60)
            paletteHeight = rootGrid.RowDefinitions[4].ActualHeight;
        compact = small;
        shell = new Grid();
        var root = rootGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,5,Auto"), Background = Brush("#F0EFEA") };
        root.RowDefinitions[2].MinHeight = small ? 240 : 300;
        root.RowDefinitions[4].Height = new GridLength(small ? 120 : paletteHeight);
        root.RowDefinitions[4].MinHeight = 90;
        root.SizeChanged += (_, _) =>
        {
            if (!compact && !paletteResized)
            {
                var columns = Math.Max(1, (int)((root.Bounds.Width - 16) / 28));
                root.RowDefinitions[4].Height = new GridLength(48 + Math.Ceiling(128.0 / columns) * 28);
            }
        };
        var menu = new Menu { Height = 30, HorizontalAlignment = HorizontalAlignment.Stretch };
        menu.Items.Add(MenuGroup("_File", ("_New|Ctrl+N", New), ("_Open…|Ctrl+O", () => _ = Open()),
            ("_Save…|Ctrl+S", () => _ = Save()), ("Build game cutscene…", () => _ = ExportDisplay()), ("Export frame PNG…", () => _ = Export(false)), ("Export all PNGs…", () => _ = Export(true))));
        menu.Items.Add(MenuGroup("_Edit", ("_Undo|Ctrl+Z", Undo), ("_Redo|Ctrl+Y", Redo)));
        menu.Items.Add(MenuGroup("F_rame", ("Add frame", () => { editor.AddFrame(false); RefreshAll(); }
        ),
            ("Duplicate frame", () => { editor.AddFrame(true); RefreshAll(); }
        ), ("Delete frame", () => { editor.DeleteFrame(); RefreshAll(); }
        ),
            ("Move earlier", () => { editor.MoveFrame(-1); RefreshAll(); }
        ), ("Move later", () => { editor.MoveFrame(1); RefreshAll(); }
        )));
        menu.Items.Add(MenuGroup("_View", ("Previous / current", () => { editor.Compare = !editor.Compare; Build(compact); }
        ),
            ("Onion skin", () => { editor.OnionSkin = !editor.OnionSkin; Build(compact); }
        ),
            ("Reset pane layout", () =>
            {
                desktopWorkspace = null; paneOrder[0] = Pane.Storyboard; paneOrder[1] = Pane.Canvas; paneOrder[2] = Pane.Inspector;
                paneWidths[0] = new(190); paneWidths[1] = new(1, GridUnitType.Star); paneWidths[2] = new(300); Build(compact);
            }
        )));
        menu.Items.Add(MenuGroup("_Canvas", ("Resize canvas…", ResizeCanvas)));
        menu.Items.Add(MenuGroup("_Palette", ("Edit selected color…", EditPaletteColor)));
        menu.Items.Add(MenuGroup("_Languages", ("Manage languages…", ManageLanguages)));
        if (small)
        {
            var groups = menu.Items.Cast<MenuItem>().ToArray(); menu.Items.Clear();
            var drawer = new MenuItem { Header = "_Menu" };
            foreach (var group in groups) drawer.Items.Add(group);
            menu.Items.Add(drawer);
        }
        root.Children.Add(menu);

        frameCount = Label(""); frameCount.MinWidth = 52; frameCount.TextAlignment = TextAlignment.Center;
        previewLanguage = new ComboBox
        {
            Name = "PreviewLanguage",
            Width = small ? 96 : 135,
            Height = 32,
            MinHeight = 32,
            Padding = new Thickness(8, 3),
            VerticalAlignment = VerticalAlignment.Center
        };
        SyncLanguages();
        previewLanguage.SelectionChanged += (_, _) =>
        { if (previewLanguage.SelectedItem is string code && code != editor.Language) { editor.Language = code; RefreshAll(); } };
        var nav = Row(Icon(PackIconMaterialKind.ChevronLeft, "Previous frame (Alt+Left)", () => SelectFrame(editor.FrameIndex - 1)),
            frameCount, Icon(PackIconMaterialKind.ChevronRight, "Next frame (Alt+Right)", () => SelectFrame(editor.FrameIndex + 1)));
        var onion = new ToggleButton { Content = "Onion skin", IsChecked = editor.OnionSkin, Height = 32, VerticalAlignment = VerticalAlignment.Center };
        onion.Click += (_, _) => { editor.OnionSkin = onion.IsChecked == true; RefreshCanvas(); };
        var opacity = new Slider
        {
            Name = "OnionOpacity",
            Minimum = 0,
            Maximum = 1,
            Value = editor.OnionOpacity,
            Width = 90,
            Height = 32,
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = editor.OnionSkin
        };
        var percent = Label($"{editor.OnionOpacity:P0}"); percent.Width = 36;
        opacity.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) { editor.OnionOpacity = opacity.Value; percent.Text = $"{opacity.Value:P0}"; RefreshCanvas(); } };
        onion.Click += (_, _) => opacity.IsEnabled = editor.OnionSkin;
        var compare = new ToggleButton { Content = "Compare", IsChecked = editor.Compare, Height = 32, IsVisible = !small, VerticalAlignment = VerticalAlignment.Center };
        compare.Click += (_, _) => { editor.Compare = compare.IsChecked == true; RefreshCanvas(); };
        var controls = new WrapPanel { Margin = new Thickness(8, 4, 8, 6), Orientation = Orientation.Horizontal };
        foreach (var group in new[] { nav, Row(Label("Language"), previewLanguage), Row(onion, opacity, percent, compare) })
        { group.Margin = new Thickness(0, 0, 12, 2); controls.Children.Add(group); }
        AddAt(root, controls, row: 1);

        var workspace = new Grid();
        if (small) { workspace.RowDefinitions = new RowDefinitions("*,5,240"); desktopWorkspace = null; }
        else
        {
            desktopWorkspace = workspace;
            for (var i = 0; i < 5; i++) workspace.ColumnDefinitions.Add(new ColumnDefinition(i % 2 == 0 ? paneWidths[i / 2] : new GridLength(5))
            { MinWidth = i % 2 == 0 ? paneOrder[i / 2] switch { Pane.Canvas => 360, Pane.Inspector => 280, _ => 180 } : 5 });
        }
        storyboard = new StackPanel { Margin = new Thickness(6), Spacing = 5 };
        var story = new DockPanel();
        var storyHeader = new StackPanel { Spacing = 6 };
        storyHeader.Children.Add(PaneHeader(Pane.Storyboard, "Storyboard"));
        var storyActions = Row(Icon(PackIconMaterialKind.Plus, "Add frame", () => { editor.AddFrame(false); RefreshAll(); }),
            Icon(PackIconMaterialKind.ContentCopy, "Duplicate frame", () => { editor.AddFrame(true); RefreshAll(); }),
            Icon(PackIconMaterialKind.DeleteOutline, "Delete frame", () => { editor.DeleteFrame(); RefreshAll(); }),
            Icon(PackIconMaterialKind.ArrowUp, "Move earlier", () => { editor.MoveFrame(-1); RefreshAll(); }),
            Icon(PackIconMaterialKind.ArrowDown, "Move later", () => { editor.MoveFrame(1); RefreshAll(); }));
        storyActions.Spacing = 2; storyActions.Margin = new Thickness(6, 0, 6, 4);
        storyHeader.Children.Add(storyActions); DockPanel.SetDock(storyHeader, Dock.Top); story.Children.Add(storyHeader);
        story.Children.Add(new ScrollViewer { Content = storyboard, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });

        var center = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        center.Children.Add(PaneHeader(Pane.Canvas, "Canvas"));
        toolOptions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 5) };
        AddAt(center, toolOptions, row: 1);
        var drawing = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        toolRail = new StackPanel { Name = "ToolRail", Margin = new Thickness(4), Spacing = 3 };
        AddAt(drawing, new ScrollViewer { Content = toolRail, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        canvas = new CutsceneCanvas { Focusable = true };
        previous = new CutsceneCanvas { IsHitTestVisible = false };
        canvas.PointerPressed += CanvasPressed; canvas.PointerMoved += CanvasMoved; canvas.PointerReleased += CanvasReleased;
        canvas.PointerCaptureLost += (_, _) => { dragging = false; shapeStart = null; creatingText = false; canvas.DraftTextBounds = null; canvas.InvalidateVisual(); };
        canvasPair = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), Margin = new Thickness(12) };
        canvasPair.Children.Add(previous);
        AddAt(canvasPair, new Border { Child = canvas, BorderBrush = Brush("#959BA1"), BorderThickness = new Thickness(1) }, 1);
        canvasFit = new Viewbox { Child = canvasPair, Stretch = Stretch.Uniform };
        canvasScroll = new ScrollViewer
        {
            Content = canvasFit,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brush("#D9DCDF")
        };
        canvasScroll.AddHandler(PointerWheelChangedEvent, ZoomWheel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddAt(drawing, canvasScroll, 1); AddAt(center, drawing, row: 2);

        inspector = new StackPanel { Margin = new Thickness(10), Spacing = 6 };
        var inspectorPane = new DockPanel(); var inspectorHeader = PaneHeader(Pane.Inspector, "Layers & text");
        DockPanel.SetDock(inspectorHeader, Dock.Top); inspectorPane.Children.Add(inspectorHeader);
        inspectorPane.Children.Add(new ScrollViewer { Content = inspector, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        if (small)
        {
            workspace.Children.Add(center);
            AddAt(workspace, new GridSplitter { ResizeDirection = GridResizeDirection.Rows, Background = Brush("#B9BDC2") }, row: 1);
            AddAt(workspace, new TabControl { Items = { new TabItem { Header = "Frames", Content = story }, new TabItem { Header = "Layers & text", Content = inspectorPane } } }, row: 2);
        }
        else
        {
            var panes = new Dictionary<Pane, Control> { [Pane.Storyboard] = story, [Pane.Canvas] = center, [Pane.Inspector] = inspectorPane };
            for (var i = 0; i < 3; i++) AddAt(workspace, panes[paneOrder[i]], i * 2);
            for (var i = 0; i < 2; i++) AddAt(workspace, new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = Brush("#B9BDC2") }, i * 2 + 1);
        }
        AddAt(root, workspace, row: 2);
        var paletteDivider = new GridSplitter { ResizeDirection = GridResizeDirection.Rows, Background = Brush("#B9BDC2") };
        paletteDivider.AddHandler(PointerReleasedEvent, (_, _) => paletteResized = true, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        AddAt(root, paletteDivider, row: 3);
        palette = new StackPanel { Margin = new Thickness(8, 5), Spacing = 5 };
        AddAt(root, new ScrollViewer { Content = palette, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, row: 4);
        shell.Children.Add(root); Content = shell; RefreshAll(); RefreshTools();
    }

    private void SyncLanguages()
    {
        editor.EnsureLanguage();
        if (previewLanguage is null) return;
        var keys = editor.Scene.Translations.Keys.Order().ToArray();
        if (previewLanguage.ItemsSource is not string[] old || !old.SequenceEqual(keys)) previewLanguage.ItemsSource = keys;
        previewLanguage.SelectedItem = editor.Language;
    }

    private void RefreshAll()
    { SyncLanguages(); RefreshCanvas(); RefreshStoryboard(); RefreshInspector(); RefreshPalette(); RefreshTitle(); }

    private void RefreshTitle()
    {
        if (TopLevel.GetTopLevel(this) is Window window)
            window.Title = $"{editor.FileName ?? "Untitled"}{(editor.IsDirty ? " *" : "")} — ComicEditor";
    }

    private void RefreshCanvas()
    {
        if (canvas is null || previous is null) return;
        canvas.Scene = editor.Scene; canvas.FrameIndex = editor.FrameIndex; canvas.Language = editor.Language;
        canvas.Width = previous.Width = editor.Scene.Width; canvas.Height = previous.Height = editor.Scene.Height;
        canvas.OnionSkin = editor.OnionSkin; canvas.OnionOpacity = editor.OnionOpacity;
        canvas.SelectedTextId = editor.SelectedTextId; canvas.ShowTextBounds = true; canvas.InvalidateVisual();
        previous.Scene = editor.Scene; previous.FrameIndex = Math.Max(0, editor.FrameIndex - 1); previous.Language = editor.Language;
        previous.IsVisible = editor.Compare && editor.FrameIndex > 0 && !compact;
        previous.Margin = previous.IsVisible ? new Thickness(0, 0, 8, 0) : default; previous.InvalidateVisual();
        if (frameCount is not null) frameCount.Text = $"{editor.FrameIndex + 1} / {editor.Scene.Frames.Count}";
        if (status is not null) status.Text = $"{editor.Scene.Width} × {editor.Scene.Height}";
        if (canvasFit is not null && canvasScroll is not null)
        {
            canvasFit.Width = zoom == 0 ? double.NaN : (editor.Scene.Width * (previous.IsVisible ? 2 : 1) + 26) * zoom;
            canvasFit.Height = zoom == 0 ? double.NaN : (editor.Scene.Height + 26) * zoom;
            canvasScroll.HorizontalScrollBarVisibility = canvasScroll.VerticalScrollBarVisibility = zoom == 0 ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        }
    }

    private void RefreshTools()
    {
        if (toolRail is null || toolOptions is null) return;
        toolRail.Children.Clear(); toolOptions.Children.Clear();
        var kinds = new[] { PackIconMaterialKind.Pencil, PackIconMaterialKind.Brush, PackIconMaterialKind.Draw, PackIconMaterialKind.Eraser,
            PackIconMaterialKind.FormatColorFill, PackIconMaterialKind.VectorLine, PackIconMaterialKind.RectangleOutline,
            PackIconMaterialKind.EllipseOutline, PackIconMaterialKind.Eyedropper, PackIconMaterialKind.FormatText };
        var all = Enum.GetValues<Tool>();
        for (var i = 0; i < all.Length; i++)
        {
            var chosen = all[i];
            var button = Icon(kinds[i], chosen + (chosen == Tool.Text ? " — drag to create a text area" : ""), () => { editor.Tool = chosen; RefreshTools(); RefreshInspector(); RefreshCanvas(); });
            button.Width = button.Height = compact ? 40 : 34;
            if (chosen == editor.Tool) { button.Background = Brush("#BBDDF5"); button.BorderBrush = Brush("#147BC1"); }
            toolRail.Children.Add(button);
        }
        toolOptions.Children.Add(Label(editor.Tool.ToString(), true));
        if (editor.Tool is Tool.Pixel or Tool.Smooth or Tool.Pressure or Tool.Eraser)
        {
            var size = new NumericUpDown { Value = editor.BrushSize, Minimum = 1, Maximum = 16, Width = 116, Height = 32, MinHeight = 32 };
            size.ValueChanged += (_, _) => editor.BrushSize = (int)(size.Value ?? 1);
            toolOptions.Children.Add(size); ToolTip.SetTip(size, "Brush size in pixels");
        }
        var scales = new[] { "Fit", "100%", "200%", "400%", $"{zoom:P0}" }.Distinct().ToArray();
        var scale = new ComboBox
        {
            ItemsSource = scales,
            SelectedItem = zoom == 0 ? "Fit" : $"{zoom:P0}",
            Width = 85,
            Height = 32,
            MinHeight = 32,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 3)
        };
        scale.SelectionChanged += (_, _) => { if (scale.SelectedIndex <= 3) zoom = scale.SelectedIndex switch { 1 => 1, 2 => 2, 3 => 4, _ => 0 }; RefreshCanvas(); };
        toolOptions.Children.Add(scale);
        status = Label($"{editor.Scene.Width} × {editor.Scene.Height}"); status.IsVisible = !compact; toolOptions.Children.Add(status);
    }

    private void SelectFrame(int index) { editor.SelectFrame(index); RefreshAll(); }
    private void Undo() { if (editor.Undo()) { RefreshAll(); RefreshTools(); } }
    private void Redo() { if (editor.Redo()) { RefreshAll(); RefreshTools(); } }
    private void New() { editor.Load(CutsceneFile.Write(Cutscene.Create())); Build(compact); }

    private void ZoomWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!(e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)) || canvasScroll is null || canvasFit is null || e.Delta.Y == 0) return;
        var point = e.GetPosition(canvasScroll);
        var origin = canvasFit.TranslatePoint(default, canvasScroll) ?? default;
        var current = zoom > 0 ? zoom : canvasPair?.TranslatePoint(new Point(1, 0), canvasScroll)?.X - canvasPair?.TranslatePoint(default, canvasScroll)?.X ?? 1;
        zoom = Math.Clamp(current * Math.Pow(1.25, e.Delta.Y), .1, 32);
        var ratio = zoom / Math.Max(.001, current);
        RefreshCanvas(); RefreshTools();
        canvasScroll.UpdateLayout();
        canvasScroll.Offset = new Vector(Math.Max(0, (point.X - origin.X) * ratio - point.X),
            Math.Max(0, (point.Y - origin.Y) * ratio - point.Y));
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (modal is not null) { if (e.Key == Key.Escape) { CloseModal(); e.Handled = true; } return; }
        var typing = e.Source is Visual visual && (visual is TextBox || visual.GetVisualAncestors().Any(v => v is TextBox or NumericUpDown));
        if (!typing && e.Key == Key.Delete && editor.SelectedText is { } selected)
        {
            editor.BeforeChange(); editor.Frame.TextObjects.Remove(selected); editor.SelectedTextId = null;
            RefreshAll(); e.Handled = true; return;
        }
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            if (e.Key == Key.S) { _ = Save(); e.Handled = true; }
            else if (e.Key == Key.O) { _ = Open(); e.Handled = true; }
            else if (e.Key == Key.N) { New(); e.Handled = true; }
            else if (!typing && e.Key == Key.Z) { if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) Redo(); else Undo(); e.Handled = true; }
            else if (!typing && e.Key == Key.Y) { Redo(); e.Handled = true; }
        }
        else if (!typing && (e.KeyModifiers.HasFlag(KeyModifiers.Alt) || e.Source == canvas))
        {
            if (e.Key == Key.Left) { SelectFrame(editor.FrameIndex - 1); e.Handled = true; }
            else if (e.Key == Key.Right) { SelectFrame(editor.FrameIndex + 1); e.Handled = true; }
        }
    }
}
