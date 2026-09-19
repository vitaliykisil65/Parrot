namespace Parrot.Core.Audio;

/// <summary>
/// The sound a card makes when it appears: two soft rising notes, synthesized as a 16-bit
/// mono WAV. Generated rather than shipped so it costs nothing on disk and can be tuned in code.
/// </summary>
public static class Chime
{
    public const int SampleRate = 44_100;

    /// <summary>Peak amplitude as a share of full scale. The prompt is a nudge, not an alarm.</summary>
    public const double Volume = 0.22;

    private static readonly (double Frequency, double StartSeconds, double LengthSeconds)[] Notes =
    [
        (987.77, 0.00, 0.35),  // B5
        (1318.51, 0.09, 0.45), // E6 — a fourth up reads as "hello", a drop would read as "error"
    ];

    public static byte[] CreateWav()
    {
        var duration = Notes.Max(n => n.StartSeconds + n.LengthSeconds);
        var samples = new short[(int)(duration * SampleRate)];

        for (var i = 0; i < samples.Length; i++)
        {
            var t = (double)i / SampleRate;
            var value = 0.0;

            foreach (var (frequency, start, length) in Notes)
            {
                var local = t - start;
                if (local < 0 || local >= length)
                    continue;

                // A 5 ms attack avoids a click; the exponential tail makes it ring like a bell.
                var envelope = Math.Min(1, local / 0.005) * Math.Exp(-local * 9) * (1 - local / length);
                value += envelope * (Math.Sin(2 * Math.PI * frequency * local) +
                                     0.25 * Math.Sin(4 * Math.PI * frequency * local));
            }

            // Two overlapping notes with a harmonic peak near 2.5; normalize before scaling.
            samples[i] = (short)Math.Round(Math.Clamp(value / 2.5 * Volume, -1, 1) * short.MaxValue);
        }

        return Encode(samples);
    }

    private static byte[] Encode(short[] samples)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        const short blockAlign = channels * bitsPerSample / 8;
        var dataLength = samples.Length * blockAlign;

        using var stream = new MemoryStream(44 + dataLength);
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);

        writer.Write("fmt "u8);
        writer.Write(16);                           // PCM header size
        writer.Write((short)1);                     // PCM
        writer.Write(channels);
        writer.Write(SampleRate);
        writer.Write(SampleRate * blockAlign);      // byte rate
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);

        writer.Write("data"u8);
        writer.Write(dataLength);
        foreach (var sample in samples)
            writer.Write(sample);

        writer.Flush();
        return stream.ToArray();
    }
}
