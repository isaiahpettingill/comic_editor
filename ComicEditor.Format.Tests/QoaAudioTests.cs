using System.Buffers.Binary;
using System.Security.Cryptography;

namespace ComicEditor.Format.Tests;

public sealed class QoaAudioTests
{
    [Theory]
    [InlineData(1, 8000, 1)]
    [InlineData(2, 44100, 44101)]
    [InlineData(8, 48000, 5143)]
    [InlineData(1, 96000, 5120)]
    public void EncodingPreservesExactSampleCountChannelsRateAndCeilingDuration(int channels, int rate, int count)
    {
        var pcm = Signal(channels, count);
        var bytes = QoaAudio.Encode(pcm, channels, rate);
        Assert.Equal("qoaf"u8.ToArray(), bytes[..4]);
        var info = QoaAudio.ReadInfo(bytes);
        Assert.Equal(channels, info.Channels); Assert.Equal(rate, info.SampleRate); Assert.Equal(count, info.SamplesPerChannel);
        Assert.Equal((int)Math.Ceiling(count * 1000d / rate), info.DurationMs);
        var decoded = QoaAudio.Decode(bytes);
        Assert.Equal(count * channels, decoded.Samples.Length);
        Assert.Equal(channels, decoded.Channels); Assert.Equal(rate, decoded.SampleRate);
        // QOA is lossy; test meaningful signal reconstruction, not PCM equality.
        Assert.True(pcm.Zip(decoded.Samples, (a, b) => Math.Abs((int)a - b)).Average() < 500);
    }

    [Fact]
    public void MatchesOfficialReferenceEncoderAndDecoderAcrossFrameBoundary()
    {
        var fixture = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "reference-3ch-22050.qoa"));
        var decoded = QoaAudio.Decode(fixture);
        Assert.Equal(new QoaAudioInfo(3, 22050, 5137), decoded.Info);
        var pcmBytes = new byte[decoded.Samples.Length * 2];
        for (int i = 0; i < decoded.Samples.Length; i++) BinaryPrimitives.WriteInt16LittleEndian(pcmBytes.AsSpan(i * 2), decoded.Samples[i]);
        Assert.Equal("ADC811FE786D3DF2C652D562E90100529DF5A47CF898DEA0F9B4685BBBA822CE", Convert.ToHexString(SHA256.HashData(pcmBytes)));
        Assert.Equal(fixture, QoaAudio.Encode(Signal(3, 5137), 3, 22050));
    }

    [Fact]
    public void CancelledEncodingDoesNotProduceAudio()
    {
        Assert.Throws<OperationCanceledException>(() => QoaAudio.Encode([0], 1, 8000, new CancellationToken(true)));
    }

    [Fact]
    public void RejectsTruncationTrailingDataAndCorruptMetadata()
    {
        var good = QoaAudio.Encode(Signal(2, 5137), 2, 44100);
        foreach (int length in new[] { 0, 7, 15, 31, good.Length - 1 })
            Assert.Throws<InvalidDataException>(() => QoaAudio.ReadInfo(good.AsSpan(0, length)));
        Assert.Throws<InvalidDataException>(() => QoaAudio.ReadInfo(good.Concat(new byte[] { 0 }).ToArray()));
        foreach (int offset in new[] { 0, 4, 8, 9, 12, 14 })
        {
            var bad = (byte[])good.Clone(); bad[offset] ^= 0xff;
            Assert.Throws<InvalidDataException>(() => QoaAudio.ReadInfo(bad));
        }
        Assert.Throws<InvalidDataException>(() => QoaAudio.Encode([], 1, 44100));
        Assert.Throws<InvalidDataException>(() => QoaAudio.Encode([0], 0, 44100));
        Assert.Throws<InvalidDataException>(() => QoaAudio.Encode([0], 1, 0));
    }

    [Theory]
    [InlineData(1, 8000)]
    [InlineData(2, 44100)]
    [InlineData(8, 48000)]
    public void WavRoundTripAndQoaImport(int channels, int rate)
    {
        var pcm = new PcmAudio(Signal(channels, 5137), channels, rate);
        var wav = WavAudio.Encode(pcm);
        var imported = WavAudio.Decode(wav);
        Assert.Equal(pcm.Samples, imported.Samples); Assert.Equal(pcm.Info, imported.Info);
        Assert.Equal(pcm.Info, QoaAudio.ReadInfo(QoaAudio.Encode(imported.Samples, channels, rate)));
        Assert.Throws<InvalidDataException>(() => WavAudio.Decode(wav[..^1]));
        var bad = (byte[])wav.Clone(); bad[32] = 1;
        Assert.Throws<InvalidDataException>(() => WavAudio.Decode(bad));
    }

    [Fact]
    public void UnsupportedWavEncodingRequestsFfmpegAndDecodingHonorsCancellation()
    {
        var wav = WavAudio.Encode(new PcmAudio([0, 1], 1, 8000));
        Assert.Throws<OperationCanceledException>(() => WavAudio.Decode(wav, new CancellationToken(true)));
        var qoa = QoaAudio.Encode([0, 1], 1, 8000);
        Assert.Throws<OperationCanceledException>(() => QoaAudio.Decode(qoa, new CancellationToken(true)));
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(20), 2); // MS ADPCM
        Assert.Throws<NotSupportedException>(() => WavAudio.Decode(wav));
    }

    [Fact]
    public void FloatWavRejectsNonFiniteSamplesAndSupportsUnknownPaddedChunks()
    {
        var wav = WavAudio.Encode(new PcmAudio([0, 0], 1, 8000));
        // Turn its four PCM bytes into a single IEEE-float sample.
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(20), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(wav.AsSpan(28), 32000);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(32), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(wav.AsSpan(34), 32);
        BinaryPrimitives.WriteSingleLittleEndian(wav.AsSpan(44), 0.5f);
        Assert.Equal(new short[] { 16384 }, WavAudio.Decode(wav).Samples);
        var padded = wav[..12].Concat("JUNK"u8.ToArray()).Concat(new byte[] { 1, 0, 0, 0, 42, 0 }).Concat(wav[12..]).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(padded.AsSpan(4), (uint)padded.Length - 8);
        Assert.Equal(new short[] { 16384 }, WavAudio.Decode(padded).Samples);
        BinaryPrimitives.WriteSingleLittleEndian(wav.AsSpan(44), float.NaN);
        Assert.Throws<InvalidDataException>(() => WavAudio.Decode(wav));
    }

    private static short[] Signal(int channels, int count) => Enumerable.Range(0, count * channels)
        .Select(i => (short)(((i / channels * 31 + i % channels * 991) % 20001) - 10000)).ToArray();
}
