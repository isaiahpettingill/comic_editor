using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ComicEditor.Format;

namespace ComicEditor.Views;

public partial class MainView
{
    private TextBox InlineFrameDuration(int index)
    {
        var scene = editor.Scene; var frame = scene.Frames[index];
        bool Current() => ReferenceEquals(editor.Scene, scene) && scene.Frames.Contains(frame);
        var input = new TextBox { Name = $"FrameTiming{index}", Width = 64, Height = compact ? 44 : 24, MinHeight = compact ? 44 : 24,
            FontSize = 12, Padding = new Thickness(4, 2), MaxLength = 12 };
        Avalonia.Automation.AutomationProperties.SetName(input, $"Frame {index + 1} duration in seconds");
        void Reset() => input.Text = (frame.DurationMs / 1000m).ToString("0.###", CultureInfo.InvariantCulture);
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
        input.LostFocus += (_, _) => Save();
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Save(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Reset(); input.ClearValue(TextBox.BorderBrushProperty); e.Handled = true; }
        };
        Reset(); return input;
    }
}
