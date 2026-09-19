using System.Text;
using Parrot.Core.Audio;

namespace Parrot.Core.Tests;

public class ChimeTests
{
    private static readonly byte[] Wav = Chime.CreateWav();

    private static short[] Samples() =>
        Enumerable.Range(0, (Wav.Length - 44) / 2)
            .Select(i => BitConverter.ToInt16(Wav, 44 + i * 2))
            .ToArray();

    [Fact]
    public void Produces_a_well_formed_pcm_wav()
    {
        Assert.Equal("RIFF", Encoding.ASCII.GetString(Wav, 0, 4));
        Assert.Equal(Wav.Length - 8, BitConverter.ToInt32(Wav, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(Wav, 8, 4));
        Assert.Equal(1, BitConverter.ToInt16(Wav, 20));                  // PCM
        Assert.Equal(1, BitConverter.ToInt16(Wav, 22));                  // mono
        Assert.Equal(Chime.SampleRate, BitConverter.ToInt32(Wav, 24));
        Assert.Equal(16, BitConverter.ToInt16(Wav, 34));
        Assert.Equal("data", Encoding.ASCII.GetString(Wav, 36, 4));
        Assert.Equal(Wav.Length - 44, BitConverter.ToInt32(Wav, 40));
    }

    [Fact]
    public void Is_short_and_quiet()
    {
        var seconds = (Wav.Length - 44) / 2.0 / Chime.SampleRate;
        var peak = Samples().Max(s => Math.Abs((int)s)) / (double)short.MaxValue;

        Assert.InRange(seconds, 0.2, 1.0);
        Assert.InRange(peak, 0.05, Chime.Volume);
    }

    [Fact]
    public void Starts_and_ends_silently_so_it_does_not_click()
    {
        var samples = Samples();

        Assert.Equal(0, samples[0]);
        Assert.InRange(Math.Abs((int)samples[^1]), 0, 50);
    }
}
