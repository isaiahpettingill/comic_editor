using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ComicEditor.Format;

namespace ComicEditor.Views;

public partial class MainView
{
    private Grid InlineFrameDuration(int index)
    {
        var scene = editor.Scene; var frame = scene.Frames[index];
        bool Current() => ReferenceEquals(editor.Scene, scene) && scene.Frames.Contains(frame);
        var input = new TextBox { Name = $"FrameTiming{index}", Width = 64, Height = compact ? 44 : 24, MinHeight = compact ? 44 : 24,
            FontSize = 12, Padding = new Thickness(4, 2), MaxLength = 12 };
        Avalonia.Automation.AutomationProperties.SetName(input, $"Frame {index + 1} duration in seconds");
        var readout = new Button { Name = $"FrameTimingDisplay{index}", Height = compact ? 44 : 24,
            MinHeight = compact ? 44 : 24, Padding = new Thickness(4, 2), FontSize = 12 };
        Avalonia.Automation.AutomationProperties.SetName(readout, $"Edit frame {index + 1} duration");
        ToolTip.SetTip(readout, "Edit duration in seconds");
        var host = new Grid { Children = { readout, input } };
        input.IsVisible = false;
        bool editing = false;
        void Reset()
        {
            input.Text = (frame.DurationMs / 1000m).ToString("0.###", CultureInfo.InvariantCulture);
            readout.Content = input.Text + " s";
        }
        void Close(bool focusReadout)
        {
            editing = false; Reset(); input.ClearValue(TextBox.BorderBrushProperty);
            readout.IsVisible = true; input.IsVisible = false;
            if (focusReadout) readout.Focus();
        }
        readout.Click += (_, _) =>
        {
            if (!Current()) return;
            Reset(); editing = true; readout.IsVisible = false; input.IsVisible = true;
            input.Focus(); input.SelectAll();
        };
        bool Save()
        {
            if (!Current()) return false;
            if (!decimal.TryParse(input.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds) ||
                seconds < frame.MinimumDurationMs / 1000m || seconds > FramePlayback.MaximumDurationMs / 1000m ||
                seconds * 1000 != decimal.Truncate(seconds * 1000))
            {
                input.BorderBrush = Brush(UiTheme.Error);
                ToolTip.SetTip(input, $"Enter {frame.MinimumDurationMs / 1000m:0.###}–3600 seconds, with at most 3 decimal places.");
                return false;
            }
            var value = (int)(seconds * 1000);
            if (frame.DurationMs != value) { FinishPath(); editor.BeforeChange(); frame.DurationMs = value; RefreshTitle(); }
            input.ClearValue(TextBox.BorderBrushProperty);
            return true;
        }
        input.LostFocus += (_, _) =>
        {
            if (!editing) return;
            // Leaving an invalid field cancels it rather than leaving an invisible
            // draft that could unexpectedly commit during later navigation.
            Save(); Close(focusReadout: false);
        };
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && editing) { if (Save()) Close(focusReadout: true); e.Handled = true; }
            else if (e.Key == Key.Escape && editing) { Close(focusReadout: true); e.Handled = true; }
        };
        Reset(); return host;
    }
}
