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
    private EditorState editor = new(PreferencesStorage.Load());
    private CutsceneCanvas? canvas, previous;
    private StackPanel? storyboard, inspector, palette, toolRail, toolOptions;
    private TextBlock? status, frameCount;
    private ComboBox? previewLanguage;
    private Grid? desktopWorkspace, rootGrid, shell, canvasPair;
    private Border? modal;
    private Control? returnFocus;
    private bool compact;
    private readonly bool touchLayout;
    private Button? undoButton, redoButton;
    private Grid? compactTopBar;
    private bool compactSingleRow;
    private double paletteHeight = 150;
    private bool paletteResized;
    private readonly Pane[] paneOrder = [Pane.Storyboard, Pane.Canvas, Pane.Inspector];
    private readonly GridLength[] paneWidths = [new(190), new(1, GridUnitType.Star), new(300)];
    private Pane? draggingPane;
    private Point paneDragStart;
    private double zoom { get => editor.Preferences.Zoom; set { editor.Preferences.Zoom = value; editor.Preferences.Save(); } } // Zero fits.
    private ScrollViewer? canvasScroll;
    private Viewbox? canvasFit;

    public MainView() : this(OperatingSystem.IsAndroid() || OperatingSystem.IsIOS()) { }

    public MainView(bool touchLayout)
    {
        this.touchLayout = touchLayout;
        activeTab = new ProjectTab(editor); tabs.Add(activeTab);
        Styles.Add(new ComicEditor.Styles.EditorScrolling(touchLayout));
        editor.FinishPendingEdit = () => FinishPath();
        InitializeComponent();
        ComicEditor.Styles.EditorThemes.Apply(editor.Preferences.Theme);
        Foreground = Brush(UiTheme.Text);
        Resources["SliderPreContentMargin"] = new GridLength(6);
        Resources["SliderPostContentMargin"] = new GridLength(6);
        Build(touchLayout);
        SizeChanged += (_, _) =>
        {
            var small = UseCompactLayout;
            if (modal is null && (small != compact || small && compactSingleRow != CompactLandscape)) Build(small);
            if (modal?.Child is Border card) card.MaxHeight = Math.Max(120, Bounds.Height - 24);
        };
        AttachedToVisualTree += (_, _) => RefreshTitle();
        AttachedToVisualTree += async (_, _) =>
        {
            var id = CutsceneFonts.Normalize(editor.Preferences.FontId);
            if (!id.StartsWith("google:") || CutsceneFonts.Ids.Contains(id)) return;
            try { await CutsceneFonts.LoadGoogleAsync("https://fonts.google.com/specimen/" + Uri.EscapeDataString(id[7..])); RefreshCanvas(); }
            catch (Exception ex) { await ShowError("Could not load the remembered font: " + ex.Message); }
        };
        KeyDown += OnKeyDown;
        AttachedToVisualTree += StartSession;
        DetachedFromVisualTree += (_, _) => { sessionTimer?.Stop(); if (SessionStorage.Flush == FlushSession) SessionStorage.Flush = null; };
        DetachedFromVisualTree += (_, _) => StopUpdates();
        DetachedFromVisualTree += (_, _) => StopSpray();
        DetachedFromVisualTree += (_, _) => ResetCanvasNavigation();
        DetachedFromVisualTree += (_, _) => { fontCheck?.Cancel(); fontInstall?.Cancel(); };
    }

    private bool UseCompactLayout => touchLayout || Bounds.Width < 900 || Bounds.Height is > 0 and < 540;
    private bool CompactLandscape => Bounds.Width >= 640 && Bounds.Height is > 0 and < 540;

    private static TextBlock Label(string text, bool bold = false) => new()
    {
        Text = text,
        FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap
    };

    private Button Button(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            Height = compact ? 44 : 32,
            Padding = new Thickness(10, 4),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        button.Click += (_, _) => action();
        return button;
    }

    private Button Icon(PackIconMaterialKind kind, string tip, Action action)
    {
        var button = Button("", action);
        button.Content = new PackIconMaterial { Kind = kind, Width = compact ? 22 : 18, Height = compact ? 22 : 18 };
        button.Width = compact ? 44 : 32; button.Padding = new Thickness(5);
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
            Background = Brush(UiTheme.Header),
            Padding = new Thickness(8, 5),
            Child = compact ? Label(caption, true) : Row(new PackIconMaterial { Kind = PackIconMaterialKind.Drag, Width = 16, Height = 16 }, Label(caption, true)),
            Cursor = new Cursor(compact ? StandardCursorType.Arrow : StandardCursorType.SizeAll)
        };
        if (!compact) ToolTip.SetTip(header, "Drag onto another pane to swap positions");
        header.PointerPressed += (_, e) =>
        {
            if (compact || desktopWorkspace is null) return;
            draggingPane = pane; paneDragStart = e.GetPosition(desktopWorkspace); e.Pointer.Capture(header);
            header.Background = Brush(UiTheme.Selection);
        };
        header.PointerReleased += (_, e) =>
        {
            var source = draggingPane; var workspace = desktopWorkspace;
            var x = workspace is null ? -1 : e.GetPosition(workspace).X;
            draggingPane = null; e.Pointer.Capture(null); header.Background = Brush(UiTheme.Header);
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

    private static SolidColorBrush Brush(string hex)
    {
        if (hex.Length == 9 && RgbaColor.IsHex(hex))
        {
            var rgba = RgbaColor.Parse(hex);
            return new(Color.FromArgb((byte)rgba, (byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8)));
        }
        return new(Color.Parse(hex));
    }

    private void Build(bool small)
    {
        EndInlineTextEdit();
        ResetCanvasNavigation();
        if (desktopWorkspace is not null && !compact)
            for (var i = 0; i < 3; i++) paneWidths[i] = desktopWorkspace.ColumnDefinitions[i * 2].Width;
        if (rootGrid is not null && !compact && rootGrid.RowDefinitions[4].ActualHeight > 60)
            paletteHeight = rootGrid.RowDefinitions[4].ActualHeight;
        compact = small;
        compactSingleRow = small && CompactLandscape;
        shell = new Grid();
        var root = rootGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,5,Auto"), Background = Brush(UiTheme.Background) };
        root.RowDefinitions[2].MinHeight = small ? 0 : 300;
        root.RowDefinitions[3].Height = new GridLength(small ? 0 : 5);
        root.RowDefinitions[4].Height = small ? GridLength.Auto : new GridLength(paletteHeight);
        root.RowDefinitions[4].MinHeight = small ? 0 : 90;
        root.SizeChanged += (_, _) =>
        {
            if (!compact && !paletteResized)
            {
                var columns = Math.Max(1, (int)((root.Bounds.Width - 16) / 28));
                root.RowDefinitions[4].Height = new GridLength(Math.Min(240, 48 + Math.Ceiling(editor.Scene.Palette.Count / (double)columns) * 28));
            }
        };
        var menu = new Menu { Height = small ? 44 : 30, HorizontalAlignment = HorizontalAlignment.Stretch };
        var header = new StackPanel { Spacing = 0 }; AddAt(root, header);
        menu.Items.Add(MenuGroup("_File", ("_New|Ctrl+N", New), ("New indexed cutscene", () => NewWithMode(false)), ("New RGBA cutscene", () => NewWithMode(true)), ("_Open…|Ctrl+O", () => _ = Open()),
            ("_Save|Ctrl+S", () => _ = Save()), ("Save _as…", () => _ = SaveAs()), ("Build game cutscene…", () => _ = ExportDisplay()),
            ("Export frame PNG…", () => _ = Export(false)), ("Export all PNGs…", () => _ = Export(true)),
            ("Export PDF…", () => ExportBook(BookFormat.Pdf)), ("Export EPUB…", () => ExportBook(BookFormat.Epub)),
            ("Export CBZ…", () => ExportBook(BookFormat.Cbz))));
        autosaveMenu = new MenuItem { Header = "Autosave & recovery…" };
        autosaveMenu.Click += (_, _) => ShowAutosave(); ((MenuItem)menu.Items[0]!).Items.Add(autosaveMenu);
        menu.Items.Add(MenuGroup("_Edit", ("_Undo|Ctrl+Z", Undo), ("_Redo|Ctrl+Y", Redo),
            ("Cut artwork|Ctrl+X", () => CopySelection(true)), ("Copy artwork|Ctrl+C", () => CopySelection(false)),
            ("Paste artwork|Ctrl+V", PasteSelection), ("Select all artwork|Ctrl+A", SelectAllArtwork),
            ("Delete selected artwork", DeleteSelection), ("Deselect", Deselect),
            ("Tool options…", EditToolOptions), ("Drawing input…", EditDrawingInput)));
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
        ((MenuItem)menu.Items[3]!).Items.Add(ThemeMenu());
        menu.Items.Add(MenuGroup("_Canvas", ("Resize canvas…", ResizeCanvas), ("Convert to RGBA color…", EnableRgbaMode)));
        menu.Items.Add(MenuGroup("_Palette", ("Palette editor…", EditPalette), ("Edit selected color…", EditPaletteColor)));
        menu.Items.Add(MenuGroup("_Languages", ("Manage languages…", ManageLanguages)));
        AddUpdateMenu(menu);
        compactDrawer = null;
        if (small)
        {
            var groups = menu.Items.Cast<MenuItem>().ToArray(); menu.Items.Clear();
            var drawer = new MenuItem { Header = new PackIconMaterial { Kind = PackIconMaterialKind.Menu, Width = 22, Height = 22 }, Height = 44, Width = 44 };
            compactDrawer = drawer; RefreshUpdateControls();
            Avalonia.Automation.AutomationProperties.SetName(drawer, "Menu");
            foreach (var group in groups) drawer.Items.Add(group);
            menu.Items.Add(drawer);
        }
        if (!small) header.Children.Add(menu);

        frameCount = Label(""); frameCount.MinWidth = 52; frameCount.TextAlignment = TextAlignment.Center;
        previewLanguage = new ComboBox
        {
            Name = "PreviewLanguage",
            Width = small ? 80 : 135,
            Height = small ? 44 : 32,
            MinHeight = small ? 44 : 32,
            Padding = new Thickness(8, 3),
            VerticalAlignment = VerticalAlignment.Center
        };
        SyncLanguages();
        previewLanguage.SelectionChanged += (_, _) =>
        { if (previewLanguage.SelectedItem is string code && code != editor.Language) { editor.Language = code; RefreshAll(); } };
        Avalonia.Automation.AutomationProperties.SetName(previewLanguage, "Preview language");
        undoButton = Icon(PackIconMaterialKind.Undo, "Undo", Undo); undoButton.Name = "UndoButton";
        redoButton = Icon(PackIconMaterialKind.Redo, "Redo", Redo); redoButton.Name = "RedoButton";
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
        foreach (var group in new[] { Row(undoButton, redoButton), nav, Row(Label("Language"), previewLanguage), Row(onion, opacity, percent, compare) })
        { group.Margin = new Thickness(0, 0, 12, 2); controls.Children.Add(group); }
        if (small)
        {
            // Reparent the small controls into a single stable row; no wrapping on rotation.
            controls.Children.Clear();
            ((StackPanel)undoButton.Parent!).Children.Clear();
            ((StackPanel)previewLanguage.Parent!).Children.Clear();
            nav.Children.Remove(frameCount);
            var top = compactTopBar = new Grid { Name = "CompactTopBar", ColumnDefinitions = new ColumnDefinitions(compactSingleRow ? "44,44,44,Auto,*,80" : "44,44,44,*,80"), Margin = new Thickness(4, 2) };
            top.Children.Add(menu); AddAt(top, undoButton, 1); AddAt(top, redoButton, 2);
            AddAt(top, frameCount, compactSingleRow ? 4 : 3); AddAt(top, previewLanguage, compactSingleRow ? 5 : 4); header.Children.Add(top);
            // The original desktop comparison group is not used on compact screens.
            ((StackPanel)onion.Parent!).Children.Clear();
            var onionSettings = new StackPanel { Spacing = 8, Children = { onion, Row(opacity, percent) } };
            compactOnionButton = Icon(PackIconMaterialKind.LayersOutline, "Onion skin settings", () => { });
            compactOnionButton.Name = "CompactOnion";
            compactOnionButton.Flyout = new Flyout { Content = onionSettings };
            onion.Height = 44;
            opacity.Height = 44; opacity.Width = 140;
        }
        else AddAt(root, controls, row: 1);
        tabStrip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(4, 1, 4, 2) };
        header.Children.Add(new ScrollViewer { Content = tabStrip, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
        RefreshTabs();

        var workspace = new Grid();
        if (small) { desktopWorkspace = null; }
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
        if (small)
        {
            var previousFrame = Button("Previous frame", () => SelectFrame(editor.FrameIndex - 1));
            var nextFrame = Button("Next frame", () => SelectFrame(editor.FrameIndex + 1));
            storyHeader.Children.Add(Row(previousFrame, nextFrame));
        }
        story.Children.Add(new ScrollViewer { Content = storyboard, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });

        var center = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        if (!small) center.Children.Add(PaneHeader(Pane.Canvas, "Canvas"));
        toolOptions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8, 5) };
        AddAt(center, small ? toolOptions : new ScrollViewer { Content = toolOptions, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }, row: 1);
        var drawing = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        toolRail = new StackPanel { Name = "ToolRail", Margin = new Thickness(4), Spacing = 3 };
        if (!small) AddAt(drawing, new ScrollViewer { Content = toolRail, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        canvas = new CutsceneCanvas { Focusable = true };
        inlineTextLayer = new Grid { Width = editor.Scene.Width, Height = editor.Scene.Height };
        inlineTextLayer.Children.Add(canvas);
        previous = new CutsceneCanvas { IsHitTestVisible = false };
        canvas.PointerPressed += CanvasPressed; canvas.PointerMoved += CanvasMoved; canvas.PointerReleased += CanvasReleased;
        canvas.PointerCaptureLost += (_, e) => { if (e.Pointer != drawingPointer) return; drawingPointer = null; StopSpray(); dragging = false; shapeStart = null; creatingText = false; canvas.DraftTextBounds = null; canvas.InvalidateVisual(); };
        canvasPair = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), Margin = new Thickness(12) };
        canvasPair.Children.Add(previous);
        AddAt(canvasPair, new Border { Child = inlineTextLayer, BorderBrush = Brush(UiTheme.Border), BorderThickness = new Thickness(1) }, 1);
        canvasFit = new Viewbox { Child = canvasPair, Stretch = Stretch.Uniform };
        canvasScroll = new DrawingViewport
        {
            Name = "CanvasViewport",
            Content = canvasFit,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brush(UiTheme.Workspace)
        };
        canvasScroll.AddHandler(PointerWheelChangedEvent, ZoomWheel, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AttachCanvasNavigation();
        AddAt(drawing, canvasScroll, 1); AddAt(center, drawing, row: 2);

        inspector = new StackPanel { Margin = new Thickness(10), Spacing = 6 };
        var inspectorPane = new DockPanel(); var inspectorHeader = PaneHeader(Pane.Inspector, "Layers & text");
        DockPanel.SetDock(inspectorHeader, Dock.Top); inspectorPane.Children.Add(inspectorHeader);
        inspectorPane.Children.Add(new ScrollViewer { Content = inspector, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        if (!small)
        {
            var panes = new Dictionary<Pane, Control> { [Pane.Storyboard] = story, [Pane.Canvas] = center, [Pane.Inspector] = inspectorPane };
            for (var i = 0; i < 3; i++) AddAt(workspace, panes[paneOrder[i]], i * 2);
            for (var i = 0; i < 2; i++) AddAt(workspace, new GridSplitter { ResizeDirection = GridResizeDirection.Columns, Background = Brush(UiTheme.Border) }, i * 2 + 1);
        }
        AddAt(root, workspace, row: 2);
        var paletteDivider = new GridSplitter { ResizeDirection = GridResizeDirection.Rows, Background = Brush(UiTheme.Border) };
        paletteDivider.AddHandler(PointerReleasedEvent, (_, _) => paletteResized = true, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        if (!small) AddAt(root, paletteDivider, row: 3);
        palette = new StackPanel { Margin = new Thickness(8, 5), Spacing = 5 };
        var palettePane = new ScrollViewer { Content = palette, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        if (small)
        {
            // Options stay above the drawing area; panels never consume its space.
            center.Children.Remove(toolOptions);
            if (compactSingleRow) AddAt(compactTopBar!, toolOptions, 3);
            else AddAt(root, toolOptions, row: 1);
            AddCompactWorkspace(root, workspace, center, story, inspectorPane, palettePane);
        }
        else AddAt(root, palettePane, row: 4);
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
    { SyncLanguages(); RefreshCanvas(); RefreshStoryboard(); RefreshInspector(); RefreshPalette(); RefreshTitle(); QueueFontCheck(); }

    private void RefreshTitle()
    {
        if (TopLevel.GetTopLevel(this) is Window window)
            window.Title = $"{editor.FileName ?? "Untitled"}{(editor.IsDirty ? " *" : "")} — ComicEditor";
        RefreshTabs();
    }

    private void RefreshCanvas()
    {
        if (undoButton is not null) undoButton.IsEnabled = editor.CanUndo;
        if (redoButton is not null) redoButton.IsEnabled = editor.CanRedo;
        if (canvas is null || previous is null) return;
        canvas.Scene = editor.Scene; canvas.FrameIndex = editor.FrameIndex; canvas.Language = editor.Language;
        canvas.Width = previous.Width = editor.Scene.Width; canvas.Height = previous.Height = editor.Scene.Height;
        if (inlineTextLayer is not null) { inlineTextLayer.Width = editor.Scene.Width; inlineTextLayer.Height = editor.Scene.Height; }
        if (inlineTextBox is not null && (editor.SelectedTextId != inlineTextObjectId || editor.Language != inlineLanguage || editor.Frame.TextObjects.All(t => t.Id != inlineTextObjectId))) EndInlineTextEdit();
        canvas.OnionSkin = editor.OnionSkin; canvas.OnionOpacity = editor.OnionOpacity;
        canvas.SelectedTextId = editor.SelectedTextId; canvas.ShowTextBounds = true; canvas.InvalidateVisual();
        if (selection?.Owner != editor.Layer || editor.Tool is not (Tool.Select or Tool.Lasso)) selection = null;
        canvas.SelectionBounds = selection is null ? null : new Rect(selection.X, selection.Y, selection.Width, selection.Height);
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
        if (inlineTextBox is not null) { RefreshInlineTextToolbar(); return; }
        toolRail.Children.Clear(); toolOptions.Children.Clear();
        if (compact) { RefreshCompactTools(); return; }
        var all = Enum.GetValues<Tool>();
        for (var i = 0; i < all.Length; i++)
        {
            var chosen = all[i];
            var button = Icon(ToolIcon(chosen), ToolName(chosen) + " — " + ToolHelp(chosen), () => ChooseTool(chosen));
            button.Width = button.Height = compact ? 40 : 34;
            if (chosen == editor.Tool) { button.Background = Brush(UiTheme.Selection); button.BorderBrush = Brush(UiTheme.Accent); }
            toolRail.Children.Add(button);
        }
        toolOptions.Children.Add(Label(ToolName(editor.Tool), true));
        if (UsesSize(editor.Tool))
        {
            var size = new NumericUpDown { Value = editor.BrushSize, Minimum = 1, Maximum = 64, Width = 120, Height = 32, MinHeight = 32 };
            size.ValueChanged += (_, _) => editor.BrushSize = (int)(size.Value ?? 1);
            toolOptions.Children.Add(size); ToolTip.SetTip(size, "Brush size in pixels");
        }
        var options = Icon(PackIconMaterialKind.Tune, "Tool options", EditToolOptions); options.Name = "ToolOptions";
        toolOptions.Children.Add(options);
        if (pathBase is not null) toolOptions.Children.Add(Button("Finish", () => FinishPath()));
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

    private void SelectFrame(int index) { EndInlineTextEdit(); FinishPath(); selection = null; editor.SelectFrame(index); RefreshAll(); }
    private void Undo() { if (pathBase is not null) { FinishPath(cancel: true); return; } var palette = editor.Scene.Palette; if (editor.Undo()) { RefreshHistory(palette); } }
    private void Redo() { FinishPath(); var palette = editor.Scene.Palette; if (editor.Redo()) { RefreshHistory(palette); } }
    private void RefreshHistory(IReadOnlyList<string> previousPalette)
    {
        if (!previousPalette.SequenceEqual(editor.Scene.Palette)) selection = clipboardSelection = null;
        editor.RememberCanvas(); editor.RememberPalette(); RefreshAll(); RefreshTools();
    }
    private void New()
    {
        if (fileBusy) return;
        var next = new EditorState(editor.Preferences);
        AddTab(next); ClearFile(); _ = SaveSessionSafely();
    }
    private void NewWithMode(bool rgba)
    {
        editor.Preferences.RgbaCanvas = rgba; editor.Preferences.Palette = null; editor.Preferences.Save(); New();
    }
    private void EnableRgbaMode()
    {
        if (editor.Scene.IsRgba) return;
        FinishPath(); editor.BeforeChange(); editor.Scene.ConvertToRgba();
        editor.Preferences.RgbaCanvas = true; editor.Preferences.Palette = editor.Scene.Palette.ToArray(); editor.Preferences.Save();
        selection = clipboardSelection = null; Build(compact);
    }

    private void ZoomWheel(object? sender, PointerWheelEventArgs e)
    {
        if (canvasScroll is null || e.Delta.Y == 0) return;
        if (!dragging && canvasTouches.Count == 0 && panPointer is null)
        {
            ZoomAt(CanvasScale * Math.Pow(1.25, e.Delta.Y), e.GetPosition(canvasScroll)); RefreshTools();
        }
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (modal is not null) { if (e.Key == Key.Escape) { CloseModal(); e.Handled = true; } return; }
        var typing = e.Source is Visual visual && (visual is TextBox || visual.GetVisualAncestors().Any(v => v is TextBox or NumericUpDown));
        if (!typing && e.Key == Key.Escape) { FinishPath(cancel: true); selection = null; RefreshCanvas(); e.Handled = true; return; }
        if (!typing && e.Key == Key.Enter && pathBase is not null) { FinishPath(); e.Handled = true; return; }
        if (!typing && e.Key == Key.F2 && editor.SelectedText is not null) { BeginInlineTextEdit(); e.Handled = true; return; }
        if (!typing && editor.SelectedText is not null && FontStep(e, out var fontDelta))
        {
            BeginInlineTextEdit(); ChangeInlineFontSize(fontDelta); e.Handled = true; return;
        }
        if (!typing && e.Key == Key.Delete && selection is not null) { DeleteSelection(); e.Handled = true; return; }
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
            else if (!typing && e.Key == Key.C) { CopySelection(false); e.Handled = true; }
            else if (!typing && e.Key == Key.X) { CopySelection(true); e.Handled = true; }
            else if (!typing && e.Key == Key.V) { PasteSelection(); e.Handled = true; }
            else if (!typing && e.Key == Key.A) { SelectAllArtwork(); e.Handled = true; }
        }
        else if (!typing && (e.KeyModifiers.HasFlag(KeyModifiers.Alt) || e.Source == canvas))
        {
            if (e.Key == Key.Left) { SelectFrame(editor.FrameIndex - 1); e.Handled = true; }
            else if (e.Key == Key.Right) { SelectFrame(editor.FrameIndex + 1); e.Handled = true; }
        }
    }
}
