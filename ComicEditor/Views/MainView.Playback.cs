using System.Diagnostics;
using ComicEditor.Audio;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ComicEditor.Format;
using ComicEditor.Rendering;
using IconPacks.Avalonia.Material;

namespace ComicEditor.Views;

public partial class MainView
{
    private DispatcherTimer? playbackTimer;
    private readonly HashSet<string> previewVariables = new(StringComparer.Ordinal);

    private FrameAudioPreview? audioPreview;
    private CancellationTokenSource? audioPreparation;
    private int audioPreviewFrame = -1;
    private void StopPlayback(bool releaseAudio = true)
    {
        playbackTimer?.Stop(); playbackTimer = null;
        audioPreview?.Stop(); audioPreviewFrame = -1;
        if (!releaseAudio) return;
        audioPreparation?.Cancel(); audioPreparation = null;
        audioPreview?.Dispose(); audioPreview = null;
    }

    private void FrameSettings(int index)
    {
        FinishPath(); EndInlineTextEdit();
        var frame = editor.Scene.Frames[index];
        var duration = new NumericUpDown { Name = "FrameDuration", Minimum = frame.MinimumDurationMs, Maximum = FramePlayback.MaximumDurationMs, Increment = 10, Value = frame.DurationMs, FormatString = "0" };
        var mode = new ComboBox { Name = "FrameShow", ItemsSource = new[] { "Always", "Never", "If variable set", "If variable not set" },
            SelectedIndex = frame.Requirement switch { "always" => 0, "never" => 1, _ when frame.Requirement.StartsWith("not ", StringComparison.Ordinal) => 3, _ => 2 }, HorizontalAlignment = HorizontalAlignment.Stretch };
        var variable = new TextBox { Name = "FrameVariable", Text = FramePlayback.Variable(frame.Requirement) ?? "", PlaceholderText = "PLAYER_A", IsEnabled = mode.SelectedIndex >= 2 };
        mode.SelectionChanged += (_, _) => variable.IsEnabled = mode.SelectedIndex >= 2;
        var warning = Label(""); warning.TextWrapping = TextWrapping.Wrap;
        ShowModal($"Frame {index + 1} settings", new StackPanel { Spacing = 10, Children =
        {
            Label(frame.AudioQoa.Length > 0 ? $"Duration (milliseconds; audio minimum {frame.MinimumDurationMs} ms)" : "Duration (milliseconds)"), duration, Label("Show"), mode, Label("Variable (case-sensitive)"), variable,
            Label("A set variable is true. Unset variables are false. Use letters, digits and underscores."), warning
        } }, () =>
        {
            var name = variable.Text?.Trim() ?? "";
            if (mode.SelectedIndex >= 2 && !FramePlayback.IsVariable(name))
            { warning.Text = "Enter a variable starting with a letter or underscore (up to 64 characters). always, never and not are reserved."; return; }
            if (duration.Value is not decimal value || value < frame.MinimumDurationMs || value > FramePlayback.MaximumDurationMs || value != decimal.Truncate(value))
            { warning.Text = $"Enter a whole number from {frame.MinimumDurationMs} to 3600000 milliseconds."; return; }
            var req = mode.SelectedIndex switch { 0 => "always", 1 => "never", 2 => name, _ => "not " + name };
            if (frame.DurationMs != (int)value || frame.Requirement != req)
            { editor.BeforeChange(); frame.DurationMs = (int)value; frame.Requirement = req; }
            CloseModal(); RefreshAll();
        });
    }

    private Button FrameOptions(int index)
    {
        var button = Button("…", () => { }); button.Name = $"FrameOptions{index}";
        button.Padding = new Thickness(3, 0); button.Width = compact ? 44 : 28; button.Height = compact ? 44 : 24;
        Avalonia.Automation.AutomationProperties.SetName(button, $"Frame {index + 1} options");
        ToolTip.SetTip(button, "Frame options");
        var settings = new MenuItem { Header = "Timing & visibility…", Name = $"FrameSettings{index}" };
        settings.Click += (_, _) => FrameSettings(index);
        var attach = new MenuItem { Header = "Attach / replace audio…", Name = $"FrameAttachAudio{index}" };
        attach.Click += async (_, _) => await AttachFrameAudio(index);
        var remove = new MenuItem { Header = "Remove audio", Name = $"FrameRemoveAudio{index}" };
        remove.Click += (_, _) => RemoveFrameAudio(index);
        button.ContextMenu = new ContextMenu { Items = { settings, attach, remove } };
        button.ContextMenu.Opened += (_, _) => remove.IsEnabled = editor.Scene.Frames[index].AudioQoa.Length != 0;
        button.Click += (_, _) => button.ContextMenu.Open(button);
        return button;
    }

    private void PreviewPlayback()
    {
        StopPlayback(); FinishPath(); EndInlineTextEdit();
        var scene = editor.Scene.Snapshot();
        var variables = FramePlayback.Variables(scene);
        previewVariables.IntersectWith(variables);
        var view = new CutsceneCanvas { Name = "PlaybackCanvas", Scene = scene, FrameIndex = 0, Language = editor.Language,
            Width = scene.Width, Height = scene.Height, IsHitTestVisible = false };
        var display = new Viewbox { Child = view, Stretch = Stretch.Uniform, Height = Math.Min(320, Math.Max(100, Bounds.Height - (compact ? 360 : 300))) };
        var caption = Label(""); caption.Name = "PlaybackStatus";
        var audioStatus = Label(scene.Frames.Any(f => f.AudioQoa.Length > 0) ? "Audio preview uses FFplay when installed; QOA audio is always included in export." : "");
        audioStatus.Name = "PlaybackAudioStatus"; audioStatus.TextWrapping = TextWrapping.Wrap;
        bool audioReady = !scene.Frames.Any(f => f.AudioQoa.Length > 0);
        FramePlayer? player = null;
        var clock = new Stopwatch(); var last = TimeSpan.Zero;
        var play = Button("Play", () => { }); play.Name = "PlaybackPlay";
        var pause = Button("Pause", () => { }); pause.Name = "PlaybackPause"; pause.IsEnabled = false;
        foreach (var control in new[] { play, pause })
        {
            control.Height = compact ? 48 : 40; control.MinWidth = compact ? 96 : 84;
            control.FontSize = 16; control.Padding = new Thickness(14, 6); control.Margin = new Thickness(0, 0, 4, 4);
        }
        void Refresh()
        {
            if (player is null || player.Finished)
            { StopPlayback(releaseAudio: false); clock.Stop(); play.IsEnabled = audioReady; pause.IsEnabled = false; caption.Text = player is null ? "" : "Playback finished"; return; }
            view.FrameIndex = player.FrameIndex; view.IsVisible = true; view.InvalidateVisual();
            var frame = scene.Frames[player.FrameIndex];
            caption.Text = $"Frame {player.FrameIndex + 1} / {scene.Frames.Count} · {frame.DurationMs} ms · {frame.Requirement}";
        }
        void SyncAudio()
        {
            if (player is null || player.Finished || audioPreviewFrame == player.FrameIndex) return;
            audioPreviewFrame = player.FrameIndex;
            var warning = audioPreview?.Start(player.FrameIndex, player.ElapsedInFrame);
            if (warning is not null) audioStatus.Text = warning;
        }
        void Reset()
        {
            StopPlayback(releaseAudio: false); clock.Reset(); last = TimeSpan.Zero;
            player = new FramePlayer(scene, previewVariables); view.IsVisible = !player.Finished; Refresh();
            if (player.Finished) caption.Text = "No frames match the selected variables.";
            play.IsEnabled = audioReady && !player.Finished; pause.IsEnabled = false;
        }
        void Tick(object? sender, EventArgs args)
        {
            if (player is null) return;
            var now = clock.Elapsed; player.Advance(now - last); last = now; Refresh(); if (playbackTimer is not null) SyncAudio();
        }
        play.Click += (_, _) =>
        {
            if (!audioReady) return;
            if (player is null || player.Finished) Reset();
            if (player is null || player.Finished) return;
            SyncAudio(); clock.Start(); playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            playbackTimer.Tick += Tick; playbackTimer.Start(); play.IsEnabled = false; pause.IsEnabled = true;
        };
        pause.Click += (_, _) => { Tick(null, EventArgs.Empty); StopPlayback(releaseAudio: false); clock.Stop(); play.IsEnabled = audioReady; pause.IsEnabled = false; };
        var restart = Button("Restart", Reset); restart.Name = "PlaybackRestart"; restart.Margin = new Thickness(0, 0, 4, 4);
        var transport = new WrapPanel { Orientation = Orientation.Horizontal, Children = { play, pause, restart } };
        var checks = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var name in variables)
        {
            var check = new CheckBox { Content = Label(name), Name = "PlaybackVar_" + name, IsChecked = previewVariables.Contains(name), Margin = new Thickness(0, 0, 12, 0) };
            check.IsCheckedChanged += (_, _) => { if (check.IsChecked == true) previewVariables.Add(name); else previewVariables.Remove(name); Reset(); };
            checks.Children.Add(check);
        }
        ShowModal("Cutscene playback", new StackPanel { Spacing = 10, Children = { Label("Set variables"), checks, display, caption, audioStatus, transport } });
        Reset();
        if (!audioReady)
        {
            var dialog = modal;
            var preparation = new CancellationTokenSource(); audioPreparation = preparation;
            audioStatus.Text = "Preparing audio preview… Close or Escape cancels.";
            _ = PrepareAudio();
            async Task PrepareAudio()
            {
                FrameAudioPreview? prepared = null;
                try
                {
                    prepared = await FrameAudioPreview.PrepareAsync(scene, preparation.Token);
                    if (preparation.IsCancellationRequested || modal != dialog || audioPreparation != preparation) return;
                    audioPreview = prepared; prepared = null;
                    audioStatus.Text = "Audio preview uses FFplay when installed; QOA is included in export.";
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException)
                {
                    if (!preparation.IsCancellationRequested && modal == dialog && audioPreparation == preparation)
                        audioStatus.Text = "Silent preview: " + ex.Message;
                }
                finally
                {
                    prepared?.Dispose();
                    if (!preparation.IsCancellationRequested && modal == dialog && audioPreparation == preparation)
                    { audioReady = true; play.IsEnabled = player is not null && !player.Finished; }
                    if (audioPreparation == preparation) audioPreparation = null;
                    preparation.Dispose();
                }
            }
        }
    }
}
