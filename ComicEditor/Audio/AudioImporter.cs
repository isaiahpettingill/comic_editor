using System.ComponentModel;
using System.Diagnostics;
using ComicEditor.Format;

namespace ComicEditor.Audio;

/// <summary>Native WAV/QOA import everywhere; optional local FFmpeg for other formats.</summary>
public static class AudioImporter
{
    public const int MaximumInputBytes = 256 * 1024 * 1024;
    public static Task<byte[]> ImportAsync(byte[] bytes, CancellationToken cancellationToken = default) => ImportAsync(bytes, "ffmpeg", cancellationToken);

    internal static async Task<byte[]> ImportAsync(byte[] bytes, string ffmpegExecutable, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length > MaximumInputBytes) throw new InvalidDataException("Audio input exceeds 256 MiB.");
        if (bytes.AsSpan().StartsWith("qoaf"u8)) { QoaAudio.ReadInfo(bytes); return bytes.ToArray(); }
        PcmAudio pcm;
        if (bytes.AsSpan().StartsWith("RIFF"u8) && bytes.Length >= 12 && bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        {
            try { pcm = await Task.Run(() => WavAudio.Decode(bytes, cancellationToken), cancellationToken); }
            catch (NotSupportedException) { pcm = await ConvertAsync(bytes, ffmpegExecutable, cancellationToken); }
        }
        else pcm = await ConvertAsync(bytes, ffmpegExecutable, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var encoded = await Task.Run(() => QoaAudio.Encode(pcm.Samples, pcm.Channels, pcm.SampleRate, cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return encoded;
    }

    private static async Task<PcmAudio> ConvertAsync(byte[] bytes, string ffmpegExecutable, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsBrowser() || OperatingSystem.IsAndroid() || OperatingSystem.IsIOS())
            throw new NotSupportedException("This platform imports PCM WAV or QOA directly. Convert other audio to WAV first, or import on a desktop with FFmpeg installed.");
        var directory = Directory.CreateTempSubdirectory("comic-audio-").FullName;
        try
        {
            var input = Path.Combine(directory, "input.audio"); var output = Path.Combine(directory, "output.wav");
            await File.WriteAllBytesAsync(input, bytes, cancellationToken);
            var start = new ProcessStartInfo(ffmpegExecutable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            // ArgumentList avoids shell interpretation. No network/media-playlist protocols are allowed.
            foreach (var arg in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-protocol_whitelist", "file,pipe", "-format_whitelist", "wav,flac,ogg,mp3,mov,aac,aiff,matroska,webm", "-i", input,
                "-map", "0:a:0", "-vn", "-c:a", "pcm_s16le", "-t", "3601", "-fs", (QoaAudio.MaximumPcmBytes * 2L).ToString(System.Globalization.CultureInfo.InvariantCulture), "-y", output }) start.ArgumentList.Add(arg);
            using var process = new Process { StartInfo = start };
            try { process.Start(); }
            catch (Win32Exception ex) { throw new NotSupportedException("Importing this audio format needs FFmpeg installed on PATH. PCM WAV and QOA work without FFmpeg.", ex); }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromMinutes(2));
            var errorTask = DrainAsync(process.StandardError); var outputTask = DrainAsync(process.StandardOutput);
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None);
                if (cancellationToken.IsCancellationRequested) throw;
                throw new IOException("FFmpeg conversion exceeded two minutes. Try a shorter audio file.");
            }
            var error = await errorTask; await outputTask;
            if (process.ExitCode != 0 || !File.Exists(output)) throw new InvalidDataException("FFmpeg could not decode this audio. " + error);
            if (new FileInfo(output).Length >= QoaAudio.MaximumPcmBytes) throw new InvalidDataException("Decoded audio exceeds 256 MiB.");
            var converted = await File.ReadAllBytesAsync(output, cancellationToken);
            return await Task.Run(() => WavAudio.Decode(converted, cancellationToken), cancellationToken);
        }
        finally { try { Directory.Delete(directory, recursive: true); } catch (IOException) { } }
    }

    private static async Task<string> DrainAsync(StreamReader reader)
    {
        var result = new System.Text.StringBuilder(); var buffer = new char[4096]; int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
            if (result.Length < 2000) result.Append(buffer, 0, Math.Min(read, 2000 - result.Length));
        return result.ToString().Trim();
    }
}
