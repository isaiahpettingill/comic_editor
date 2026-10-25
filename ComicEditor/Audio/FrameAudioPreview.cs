using System.Diagnostics;
using System.Globalization;
using ComicEditor.Format;

namespace ComicEditor.Audio;

/// <summary>Prepare bounded WAV files off the UI thread; preview via optional desktop FFplay.</summary>
internal sealed class FrameAudioPreview : IDisposable
{
    private Process? process;
    private string? directory;
    private readonly Dictionary<int, (string Path, int DurationMs)> prepared = [];
    private readonly List<Task> stopping = [];
    private bool disposed;

    public static async Task<FrameAudioPreview> PrepareAsync(Cutscene scene, CancellationToken cancellationToken)
    {
        var preview = new FrameAudioPreview();
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                long total = 0;
                foreach (var frame in scene.Frames.Where(f => f.AudioQoa.Length > 0))
                {
                    var info = QoaAudio.ReadInfo(frame.AudioQoa);
                    total += (long)info.SamplesPerChannel * info.Channels * 2 + 44;
                    if (total > 512L * 1024 * 1024) throw new IOException("Audio preview needs more than 512 MiB of temporary WAV files. Embedded QOA export is unaffected.");
                }
                if (total == 0) return;
                if (OperatingSystem.IsBrowser() || OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
                    throw new NotSupportedException("Audible preview requires FFplay on desktop. Embedded QOA export is unaffected.");
                preview.directory = Directory.CreateTempSubdirectory("comic-preview-").FullName;
                for (int i = 0; i < scene.Frames.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var qoa = scene.Frames[i].AudioQoa; if (qoa.Length == 0) continue;
                    var pcm = QoaAudio.Decode(qoa, cancellationToken);
                    var path = Path.Combine(preview.directory, i.ToString(CultureInfo.InvariantCulture) + ".wav");
                    File.WriteAllBytes(path, WavAudio.Encode(pcm, cancellationToken));
                    preview.prepared[i] = (path, pcm.Info.DurationMs);
                }
                cancellationToken.ThrowIfCancellationRequested();
            }, cancellationToken);
            return preview;
        }
        catch { preview.Dispose(); throw; }
    }

    public string? Start(int frameIndex, TimeSpan offset)
    {
        Stop();
        if (disposed || !prepared.TryGetValue(frameIndex, out var audio) || offset.TotalMilliseconds >= audio.DurationMs) return null;
        try
        {
            var start = new ProcessStartInfo("ffplay") { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-nostats", "-loglevel", "quiet", "-nodisp", "-autoexit", "-ss", offset.TotalSeconds.ToString("0.######", CultureInfo.InvariantCulture), audio.Path })
                start.ArgumentList.Add(arg);
            process = Process.Start(start);
            if (process is null) throw new IOException("Could not start FFplay.");
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            return null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or NotSupportedException or InvalidOperationException)
        { Stop(); return "Silent preview: install FFplay on PATH for audio. Embedded QOA export is unaffected."; }
    }

    // Stop playback without discarding prepared files, so Pause/Restart are immediate.
    public void Stop()
    {
        var old = process; process = null;
        if (old is null) return;
        try { if (!old.HasExited) old.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
        stopping.RemoveAll(task => task.IsCompleted);
        stopping.Add(FinishProcessAsync(old));
    }

    private static async Task FinishProcessAsync(Process old)
    {
        try { await old.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException) { }
        finally { old.Dispose(); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Stop();
        var path = directory; directory = null; prepared.Clear();
        if (path is not null) _ = CleanupAsync(path, stopping.ToArray());
    }

    private static async Task CleanupAsync(string path, Task[] tasks)
    {
        await Task.WhenAll(tasks);
        try { Directory.Delete(path, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
