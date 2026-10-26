using System.Buffers.Binary;

namespace ComicEditor.Format;

public sealed record QoaAudioInfo(int Channels, int SampleRate, int SamplesPerChannel)
{
    public int DurationMs => checked((int)(((long)SamplesPerChannel * 1000 + SampleRate - 1) / SampleRate));
}

public sealed record PcmAudio(short[] Samples, int Channels, int SampleRate)
{
    public QoaAudioInfo Info => new(Channels, SampleRate, Samples.Length / Channels);
}

/// <summary>Static-file QOA codec, ported from Dominic Szablewski's MIT-licensed qoa.h.
/// See licenses/QOA-MIT.txt. Channels are interleaved; sample counts are per channel.</summary>
public static class QoaAudio
{
    public const int MaximumPcmBytes = 256 * 1024 * 1024;
    private const int FrameLength = 5120;
    private static readonly int[] Scale = [1,7,21,45,84,138,211,304,421,562,731,928,1157,1419,1715,2048];
    private static readonly int[] Reciprocal = [65536,9363,3121,1457,781,475,311,216,156,117,90,71,57,47,39,32];
    private static readonly int[] Quant = [7,7,7,5,5,3,3,1,0,0,2,2,4,4,6,6,6];
    private static readonly int[][] Dequant = Scale.Select(s => new[] { .75,-.75,2.5,-2.5,4.5,-4.5,7,-7 }
        .Select(v => (int)Math.Round(s * v, MidpointRounding.AwayFromZero)).ToArray()).ToArray();

    private sealed class Lms
    {
        public int[] History = new int[4];
        public int[] Weights = [0,0,-8192,16384];
        public Lms Copy() => new() { History = (int[])History.Clone(), Weights = (int[])Weights.Clone() };
        public int Predict()
        {
            int sum = 0;
            for (var i = 0; i < 4; i++) sum = unchecked(sum + Weights[i] * History[i]);
            return sum >> 13;
        }
        public void Update(int sample, int residual)
        {
            var delta = residual >> 4;
            for (var i = 0; i < 4; i++) Weights[i] = unchecked(Weights[i] + (History[i] < 0 ? -delta : delta));
            for (var i = 0; i < 3; i++) History[i] = History[i + 1];
            History[3] = sample;
        }
    }

    public static void ValidateInfo(QoaAudioInfo info)
    {
        if (info.Channels is < 1 or > 8 || info.SampleRate is < 1 or > 0xffffff || info.SamplesPerChannel < 1 ||
            (long)info.SamplesPerChannel * info.Channels * 2 > MaximumPcmBytes ||
            (long)info.SamplesPerChannel > (long)info.SampleRate * 3600)
            throw new InvalidDataException("Audio must contain 1–8 channels, at most one hour, and at most 256 MiB of decoded PCM.");
    }

    // Validates every frame without allocating decoded PCM (safe for project loading).
    public static QoaAudioInfo ReadInfo(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 16 || !bytes[..4].SequenceEqual("qoaf"u8)) throw Invalid();
        var count = BinaryPrimitives.ReadUInt32BigEndian(bytes[4..]);
        if (count > int.MaxValue) throw Invalid();
        var info = new QoaAudioInfo(bytes[8], Rate(bytes[9..]), (int)count);
        ValidateInfo(info);
        int offset = 8, samples = 0;
        while (samples < info.SamplesPerChannel)
        {
            if (bytes.Length - offset < 8) throw Invalid();
            var frame = bytes[offset..];
            int n = BinaryPrimitives.ReadUInt16BigEndian(frame[4..]);
            int size = BinaryPrimitives.ReadUInt16BigEndian(frame[6..]);
            if (frame[0] != info.Channels || Rate(frame[1..]) != info.SampleRate || n != Math.Min(FrameLength, info.SamplesPerChannel - samples) ||
                size != 8 + 16 * info.Channels + ((n + 19) / 20) * 8 * info.Channels || size > frame.Length) throw Invalid();
            offset += size; samples += n;
        }
        if (offset != bytes.Length) throw Invalid();
        return info;
    }

    public static byte[] Encode(short[] samples, int channels, int sampleRate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (channels is < 1 or > 8 || samples.Length % channels != 0) throw Invalid();
        var info = new QoaAudioInfo(channels, sampleRate, samples.Length / channels);
        ValidateInfo(info);
        using var output = new MemoryStream();
        Write64(output, (0x716f6166UL << 32) | (uint)info.SamplesPerChannel);
        var states = Enumerable.Range(0, channels).Select(_ => new Lms()).ToArray();
        for (int start = 0; start < info.SamplesPerChannel; start += FrameLength)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int length = Math.Min(FrameLength, info.SamplesPerChannel - start);
            int size = 8 + 16 * channels + ((length + 19) / 20) * 8 * channels;
            Write64(output, ((ulong)channels << 56) | ((ulong)sampleRate << 32) | ((ulong)length << 16) | (uint)size);
            foreach (var state in states)
            {
                // Serialized state is signed 16-bit, as specified by QOA.
                for (int i = 0; i < 4; i++) state.Weights[i] = unchecked((short)state.Weights[i]);
                WriteState(output, state.History); WriteState(output, state.Weights);
            }
            var previous = new int[channels];
            for (int sliceStart = 0; sliceStart < length; sliceStart += 20)
            for (int c = 0; c < channels; c++)
            {
                int sliceLength = Math.Min(20, length - sliceStart), bestScale = 0;
                long bestRank = long.MaxValue; ulong bestSlice = 0; Lms? bestState = null;
                for (int trial = 0; trial < 16; trial++)
                {
                    int sf = (trial + previous[c]) & 15;
                    var state = states[c].Copy(); ulong slice = (uint)sf; long rank = 0;
                    for (int i = 0; i < sliceLength; i++)
                    {
                        int sample = samples[(start + sliceStart + i) * channels + c];
                        int prediction = state.Predict(), residual = sample - prediction;
                        int scaled = unchecked(residual * Reciprocal[sf] + 32768) >> 16;
                        scaled += Math.Sign(residual) - Math.Sign(scaled);
                        int quantized = Quant[Math.Clamp(scaled, -8, 8) + 8];
                        int dequantized = Dequant[sf][quantized];
                        int reconstructed = Math.Clamp(prediction + dequantized, short.MinValue, short.MaxValue);
                        int weights = 0;
                        foreach (int weight in state.Weights) weights = unchecked(weights + weight * weight);
                        long penalty = Math.Max(0, (weights >> 18) - 0x8ff);
                        long error = sample - reconstructed;
                        rank += error * error + penalty * penalty;
                        if (rank > bestRank) break;
                        state.Update(reconstructed, dequantized);
                        slice = (slice << 3) | (uint)quantized;
                    }
                    if (rank < bestRank) { bestRank = rank; bestSlice = slice; bestState = state; bestScale = sf; }
                }
                previous[c] = bestScale; states[c] = bestState!;
                Write64(output, bestSlice << ((20 - sliceLength) * 3));
            }
        }
        return output.ToArray();
    }

    public static PcmAudio Decode(byte[] bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = ReadInfo(bytes);
        var samples = new short[info.SamplesPerChannel * info.Channels];
        int offset = 8;
        for (int start = 0; start < info.SamplesPerChannel; start += FrameLength)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int length = Math.Min(FrameLength, info.SamplesPerChannel - start);
            offset += 8;
            var states = new Lms[info.Channels];
            for (int c = 0; c < info.Channels; c++)
            {
                var state = states[c] = new Lms();
                for (int i = 0; i < 4; i++, offset += 2) state.History[i] = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(offset));
                for (int i = 0; i < 4; i++, offset += 2) state.Weights[i] = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(offset));
            }
            for (int sliceStart = 0; sliceStart < length; sliceStart += 20)
            for (int c = 0; c < info.Channels; c++)
            {
                ulong slice = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(offset)); offset += 8;
                int sf = (int)(slice >> 60); slice <<= 4;
                for (int i = 0; i < Math.Min(20, length - sliceStart); i++)
                {
                    int residual = Dequant[sf][(int)(slice >> 61)]; slice <<= 3;
                    int sample = Math.Clamp(states[c].Predict() + residual, short.MinValue, short.MaxValue);
                    samples[(start + sliceStart + i) * info.Channels + c] = (short)sample;
                    states[c].Update(sample, residual);
                }
            }
        }
        return new(samples, info.Channels, info.SampleRate);
    }

    private static int Rate(ReadOnlySpan<byte> value) => value[0] << 16 | value[1] << 8 | value[2];
    private static InvalidDataException Invalid() => new("Invalid or unsupported QOA audio.");
    private static void WriteState(Stream stream, int[] values)
    {
        ulong packed = 0;
        foreach (int value in values) packed = (packed << 16) | unchecked((ushort)value);
        Write64(stream, packed);
    }
    private static void Write64(Stream stream, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteUInt64BigEndian(bytes, value); stream.Write(bytes);
    }
}
