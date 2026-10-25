using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using ComicEditor.Audio;
using ComicEditor.Format;
using ComicEditor.Rendering;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public partial class CanvasTests
{
    [Fact]
    public async Task InlineTimingValidatesAudioMinimumAndSupportsUndoAndEscape()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var state = State(view); state.Load(CutsceneFile.Write(Cutscene.Create(2, 2)), "test.ctsc");
            state.Frame.AttachAudio(QoaAudio.Encode(new short[8001], 1, 8000));
            var window = new Window { Content = view, Width = 1200, Height = 800 }; window.Show(); Invoke(view, "RefreshAll"); _ = Capture(window);
            SaveCapture(window, "COMIC_TIMING_AUDIO_SCREENSHOT");
            var timing = Named<TextBox>(window, "FrameTiming0"); timing.Focus(); timing.Text = "1";
            timing.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal(4000, state.Frame.DurationMs);
            timing.Text = "1.001"; timing.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal(1001, state.Frame.DurationMs); Assert.True(state.Undo()); Assert.Equal(4000, state.Frame.DurationMs);
            Assert.True(state.Redo()); Assert.Equal(1001, state.Frame.DurationMs);
            Invoke(view, "RefreshAll"); _ = Capture(window); timing = Named<TextBox>(window, "FrameTiming0");
            timing.Text = "broken"; timing.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.Equal("1.001", timing.Text);
            Invoke(view, "RemoveFrameAudio", 0); Assert.Empty(state.Frame.AudioQoa); Assert.Equal(1001, state.Frame.DurationMs);
            Assert.True(state.Undo()); Assert.NotEmpty(state.Frame.AudioQoa);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EditingModalEnterUsesValidationAndSavesWithoutClicking()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var state = State(view);
            var window = new Window { Content = view, Width = 1200, Height = 800 }; window.Show();
            Invoke(view, "FrameSettings", 0); _ = Capture(window);
            var variable = Named<TextBox>(window, "FrameVariable"); Named<ComboBox>(window, "FrameShow").SelectedIndex = 2;
            variable.Text = "1INVALID"; variable.Focus();
            variable.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.NotNull(Named<Button>(window, "ModalApply")); Assert.Equal("always", state.Frame.Requirement);
            variable.Text = "READY"; variable.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal("READY", state.Frame.Requirement);
            Invoke(view, "FrameSettings", 0); _ = Capture(window);
            var duration = Named<NumericUpDown>(window, "FrameDuration"); duration.Value = 5000;
            duration.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal(5000, state.Frame.DurationMs); window.Close();
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AudioBytesAndMinimumDurationArePreservedInBothRuntimeExports(bool rgba)
    {
        var scene = Cutscene.Create(2, 2, rgba); var audio = QoaAudio.Encode(new short[8001], 1, 8000);
        scene.Frames[0].DurationMs = 100; scene.Frames[0].AttachAudio(audio);
        var display = DisplayCompiler.Compile(scene); Assert.Equal(audio, display.Frames[0].AudioQoa.ToByteArray());
        Assert.Equal(1001u, display.Frames[0].DurationMs);
        var bytes = CborExporter.Compile(scene); var offset = 0;
        var root = (Dictionary<string, object>)Read(bytes, ref offset);
        var frame = (Dictionary<string, object>)((List<object>)root["frames"])[0];
        Assert.Equal(audio, (byte[])frame["audio_qoa"]); Assert.Equal(1001u, frame["duration_ms"]);
    }

    [Fact]
    public async Task NativeWavAndQoaImportWorkWithoutExternalToolsAndCancellationDoesNotReturnAudio()
    {
        var pcm = new PcmAudio(new short[44101 * 2], 2, 44100);
        var qoa = await AudioImporter.ImportAsync(WavAudio.Encode(pcm));
        Assert.Equal(pcm.Info, QoaAudio.ReadInfo(qoa)); Assert.Equal(qoa, await AudioImporter.ImportAsync(qoa));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AudioImporter.ImportAsync(qoa, cancel.Token));
        await Assert.ThrowsAsync<InvalidDataException>(() => AudioImporter.ImportAsync("RIFF0000WAVE"u8.ToArray()));
    }
    [Fact]
    public async Task MissingFfmpegIsActionableAndDoesNotAffectNativeWavImport()
    {
        var missing = Path.Combine(Path.GetTempPath(), "missing-ffmpeg-" + Guid.NewGuid().ToString("N"));
        var error = await Assert.ThrowsAsync<NotSupportedException>(() => AudioImporter.ImportAsync([1, 2, 3], missing));
        Assert.Contains("FFmpeg installed on PATH", error.Message);
        var qoa = await AudioImporter.ImportAsync(WavAudio.Encode(new PcmAudio(new short[80], 1, 8000)), missing);
        Assert.Equal(10, QoaAudio.ReadInfo(qoa).DurationMs);
    }

    [Fact]
    public async Task InstalledFfmpegConvertsFlacWithoutChangingSampleCountRateOrChannels()
    {
        // Opt-in integration test: the regular suite also tests the missing-executable path.
        if (Environment.GetEnvironmentVariable("COMIC_TEST_FFMPEG") != "1") return;
        var fixture = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "audio", "tone.flac"));
        var qoa = await AudioImporter.ImportAsync(fixture);
        Assert.Equal(new QoaAudioInfo(1, 22050, 2205), QoaAudio.ReadInfo(qoa));
    }

    [Fact]
    public async Task ModalMultilineEnterAndInvalidNumericTextDoNotSilentlySubmit()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var state = State(view);
            var window = new Window { Content = view, Width = 1200, Height = 800 }; window.Show();
            var text = new TextBox { AcceptsReturn = true, Text = "Line one" }; var saved = 0;
            Invoke(view, "ShowModal", "Edit text", text, (Action)(() => { saved++; Invoke(view, "CloseModal"); }), "Save");
            _ = Capture(window); text.Focus();
            text.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter }); Assert.Equal(0, saved);
            text.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, KeyModifiers = KeyModifiers.Shift }); Assert.Equal(0, saved);
            text.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, KeyModifiers = KeyModifiers.Control }); Assert.Equal(1, saved);
            Invoke(view, "FrameSettings", 0); _ = Capture(window);
            var duration = Named<NumericUpDown>(window, "FrameDuration"); duration.Text = "invalid";
            duration.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal(4000, state.Frame.DurationMs); Assert.NotNull(Named<Button>(window, "ModalApply"));
            Invoke(view, "CloseModal"); window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DetachedTimingFieldCannotEditAnotherFrameAfterReorderOrDelete()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var state = State(view);
            state.Scene.Frames.Add(Frame.Create(state.Scene.Width, state.Scene.Height));
            var removed = state.Scene.Frames[1];
            var window = new Window { Content = view, Width = 1200, Height = 800 }; window.Show(); Invoke(view, "RefreshAll"); _ = Capture(window);
            var input = Named<TextBox>(window, "FrameTiming1"); input.Text = "2";
            state.Scene.Frames.Remove(removed);
            input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Assert.Equal(4000, state.Scene.Frames[0].DurationMs); window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CancellationTerminatesConverterAndRemovesTemporaryFiles()
    {
        if (OperatingSystem.IsWindows()) return; // POSIX fake executable; production uses ArgumentList on all desktops.
        var directory = Directory.CreateTempSubdirectory("comic-converter-test-").FullName;
        try
        {
            var executable = Path.Combine(directory, "converter with spaces");
            await File.WriteAllTextAsync(executable, "#!/bin/sh\nexec sleep 60\n");
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var before = Directory.GetDirectories(Path.GetTempPath(), "comic-audio-*").ToHashSet();
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AudioImporter.ImportAsync([1, 2, 3], executable, cancel.Token));
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
            Assert.All(Directory.GetDirectories(Path.GetTempPath(), "comic-audio-*"), path => Assert.Contains(path, before));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task DetachingEditorCancelsPendingAudioImport()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(TestApp));
        await session.Dispatch(() =>
        {
            var view = new MainView(); var window = new Window { Content = view, Width = 1200, Height = 800 }; window.Show();
            using var cancellation = new CancellationTokenSource();
            typeof(MainView).GetField("audioImport", PrivateInstance)!.SetValue(view, cancellation);
            window.Close(); Assert.True(cancellation.IsCancellationRequested);
        }, CancellationToken.None);
    }

}
