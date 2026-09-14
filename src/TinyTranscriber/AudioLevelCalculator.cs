using System.Buffers.Binary;

namespace TinyTranscriber;

internal static class AudioLevelCalculator
{
    public static float Calculate(ReadOnlySpan<byte> audio)
    {
        if (audio.Length < sizeof(short))
        {
            return 0;
        }

        long sumOfSquares = 0;
        var sampleCount = audio.Length / sizeof(short);

        for (var index = 0; index < sampleCount * sizeof(short); index += sizeof(short))
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(audio[index..]);
            sumOfSquares += (long)sample * sample;
        }

        var rootMeanSquare = Math.Sqrt((double)sumOfSquares / sampleCount) / short.MaxValue;
        return Math.Clamp((float)(rootMeanSquare * 4.5), 0, 1);
    }
}
