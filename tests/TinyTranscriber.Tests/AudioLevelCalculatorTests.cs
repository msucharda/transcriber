using System.Buffers.Binary;

namespace TinyTranscriber.Tests;

public sealed class AudioLevelCalculatorTests
{
    [Fact]
    public void CalculateReturnsZeroForSilence()
    {
        Assert.Equal(0, AudioLevelCalculator.Calculate(new byte[32]));
    }

    [Fact]
    public void CalculateRespondsToAudioAmplitude()
    {
        var quiet = CreateSamples(1500);
        var loud = CreateSamples(12000);

        var quietLevel = AudioLevelCalculator.Calculate(quiet);
        var loudLevel = AudioLevelCalculator.Calculate(loud);

        Assert.InRange(quietLevel, 0.1f, 0.3f);
        Assert.InRange(loudLevel, 0.9f, 1);
        Assert.True(loudLevel > quietLevel);
    }

    private static byte[] CreateSamples(short amplitude)
    {
        var bytes = new byte[64];

        for (var index = 0; index < bytes.Length; index += sizeof(short))
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(index), amplitude);
        }

        return bytes;
    }
}
