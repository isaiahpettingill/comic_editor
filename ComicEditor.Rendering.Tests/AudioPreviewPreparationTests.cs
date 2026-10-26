using System.Reflection;
using ComicEditor.Format;
using ComicEditor.Views;

namespace ComicEditor.Rendering.Tests;

public sealed class AudioPreviewPreparationTests
{
    private static readonly Type PreviewType = typeof(MainView).Assembly.GetType("ComicEditor.Audio.FrameAudioPreview")!;

    [Fact]
    public async Task PreparationCreatesReusableWavAndDisposeCleansItWithoutLaunchingPlayer()
    {
        var scene = Cutscene.Create(1, 1);
        scene.Frames[0].AttachAudio(QoaAudio.Encode(new short[8001], 1, 8000));
        var task = (Task)PreviewType.GetMethod("PrepareAsync")!.Invoke(null, [scene, CancellationToken.None])!;
        await task;
        var preview = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var directory = (string)PreviewType.GetField("directory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(preview)!;
        Assert.True(Directory.Exists(directory));
        var wav = WavAudio.Decode(await File.ReadAllBytesAsync(Path.Combine(directory, "0.wav")));
        Assert.Equal(new QoaAudioInfo(1, 8000, 8001), wav.Info);
        PreviewType.GetMethod("Stop")!.Invoke(preview, null);
        Assert.True(Directory.Exists(directory)); // Pause/Restart retain prepared files.
        Assert.Null(PreviewType.GetMethod("Start")!.Invoke(preview, [0, TimeSpan.FromSeconds(2)])); // Already past audio: no FFplay needed.
        ((IDisposable)preview).Dispose();
        Assert.False(Directory.Exists(directory));
        ((IDisposable)preview).Dispose(); // Idempotent Close/Detach.
    }

    [Fact]
    public async Task CancelledPreparationDoesNotReturnAPreview()
    {
        var scene = Cutscene.Create(1, 1);
        var task = (Task)PreviewType.GetMethod("PrepareAsync")!.Invoke(null, [scene, new CancellationToken(true)])!;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}
