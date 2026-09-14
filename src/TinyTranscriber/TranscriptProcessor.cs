using Azure.Identity;

namespace TinyTranscriber;

internal sealed record TranscriptResult(string Original, string Text, string? CleanupError = null);

internal static class TranscriptProcessor
{
    public static async Task<TranscriptResult> ProcessAsync(
        string original,
        bool cleanupEnabled,
        Func<string, CancellationToken, Task<string>> cleanup,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(original);
        cancellationToken.ThrowIfCancellationRequested();
        if (!cleanupEnabled)
        {
            return new TranscriptResult(original, original);
        }

        try
        {
            var text = await cleanup(original, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new InvalidDataException("Text cleanup returned empty text.");
            }

            return new TranscriptResult(original, text);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or InvalidDataException or TimeoutException
                or AuthenticationFailedException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new TranscriptResult(original, original, exception.Message);
        }
    }
}
