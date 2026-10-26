using System.Buffers.Binary;

namespace ComicEditor.Format;

/// <summary>Bounded RIFF/WAVE PCM and IEEE-float reader; unsupported encodings can be imported via FFmpeg.</summary>
public static class WavAudio
{
    public static PcmAudio Decode(byte[] bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bytes.Length < 12 || !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
            throw Invalid();
        long declaredEnd = (long)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)) + 8;
        if (declaredEnd > bytes.Length || declaredEnd < 12) throw Invalid();
        int channels = 0, rate = 0, bits = 0, encoding = 0, block = 0;
        ReadOnlySpan<byte> data = default;
        bool haveFormat = false, haveData = false;
        for (long position = 12; position < declaredEnd;)
        {
            if (declaredEnd - position < 8) throw Invalid();
            var chunk = bytes.AsSpan((int)position);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            long next = position + 8 + size;
            if (next > declaredEnd || size > int.MaxValue) throw Invalid();
            var payload = chunk.Slice(8, (int)size);
            if (chunk[..4].SequenceEqual("fmt "u8))
            {
                if (haveFormat || size < 16) throw Invalid();
                haveFormat = true;
                encoding = BinaryPrimitives.ReadUInt16LittleEndian(payload);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]);
                uint sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]);
                if (sampleRate > int.MaxValue) throw Invalid();
                rate = (int)sampleRate;
                block = BinaryPrimitives.ReadUInt16LittleEndian(payload[12..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(payload[14..]);
                if (encoding == 0xfffe)
                {
                    // WAVE_FORMAT_EXTENSIBLE: require the standard PCM/IEEE GUID tail.
                    if (size < 40 || BinaryPrimitives.ReadUInt16LittleEndian(payload[16..]) < 22 ||
                        !payload.Slice(26, 14).SequenceEqual(new byte[] {0,0,0,0,0x10,0,0x80,0,0,0xaa,0,0x38,0x9b,0x71}) ||
                        BinaryPrimitives.ReadUInt16LittleEndian(payload[18..]) != bits) throw Invalid();
                    encoding = BinaryPrimitives.ReadUInt16LittleEndian(payload[24..]);
                }
            }
            else if (chunk[..4].SequenceEqual("data"u8))
            {
                if (haveData) throw Invalid();
                data = payload; haveData = true;
            }
            position = next + (size & 1);
            if (position > declaredEnd) throw Invalid();
        }
        if (!haveFormat || !haveData || channels is < 1 or > 8) throw Invalid();
        if (!(encoding == 1 && bits is 8 or 16 or 24 or 32 || encoding == 3 && bits is 32 or 64))
            throw new NotSupportedException("Unsupported WAV encoding. Use PCM/IEEE-float WAV or install FFmpeg to import this file.");
        if (block != channels * ((bits + 7) / 8) || block == 0 || data.Length % block != 0) throw Invalid();
        var info = new QoaAudioInfo(channels, rate, data.Length / block); QoaAudio.ValidateInfo(info);
        var samples = new short[info.SamplesPerChannel * channels];
        for (int i = 0, p = 0; i < samples.Length; i++, p += bits / 8)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            int value;
            if (encoding == 3)
            {
                double number = bits == 32 ? BinaryPrimitives.ReadSingleLittleEndian(data[p..]) : BinaryPrimitives.ReadDoubleLittleEndian(data[p..]);
                if (!double.IsFinite(number)) throw Invalid();
                value = (int)Math.Round(Math.Clamp(number, -1, 1) * 32768);
            }
            else value = bits switch
            {
                8 => (data[p] - 128) << 8,
                16 => BinaryPrimitives.ReadInt16LittleEndian(data[p..]),
                24 => ((data[p] << 8 | data[p + 1] << 16 | data[p + 2] << 24) >> 16),
                _ => BinaryPrimitives.ReadInt32LittleEndian(data[p..]) >> 16
            };
            samples[i] = (short)Math.Clamp(value, short.MinValue, short.MaxValue);
        }
        return new(samples, channels, rate);
    }

    public static byte[] Encode(PcmAudio audio, CancellationToken cancellationToken = default)
    {
        if (audio.Channels is < 1 or > 8 || audio.Samples.Length % audio.Channels != 0) throw Invalid();
        QoaAudio.ValidateInfo(audio.Info);
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
        writer.Write("RIFF"u8); writer.Write(36 + audio.Samples.Length * 2); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)audio.Channels); writer.Write(audio.SampleRate);
        writer.Write(checked(audio.SampleRate * audio.Channels * 2)); writer.Write((ushort)(audio.Channels * 2)); writer.Write((ushort)16);
        writer.Write("data"u8); writer.Write(audio.Samples.Length * 2);
        for (int i = 0; i < audio.Samples.Length; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            writer.Write(audio.Samples[i]);
        }
        return output.ToArray();
    }
    private static InvalidDataException Invalid() => new("Invalid or unsupported WAV audio.");
}
