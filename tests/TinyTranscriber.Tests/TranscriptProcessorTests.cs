using Azure.Identity;

namespace TinyTranscriber.Tests;

public sealed class TranscriptProcessorTests
{
    [Fact]
    public async Task DisabledCleanupDoesNotCallService()
    {
        var result = await TranscriptProcessor.ProcessAsync(
            "Original", false, (_, _) => throw new InvalidOperationException("Must not call"));
        Assert.Equal("Original", result.Original);
        Assert.Equal("Original", result.Text);
        Assert.Null(result.CleanupError);
    }

    [Fact]
    public async Task SuccessfulCleanupKeepsOriginalAvailable()
    {
        var result = await TranscriptProcessor.ProcessAsync(
            "Original", true, (text, _) =>
            {
                Assert.Equal("Original", text);
                return Task.FromResult("Cleaned");
            });
        Assert.Equal("Original", result.Original);
        Assert.Equal("Cleaned", result.Text);
        Assert.Null(result.CleanupError);
    }

    public static TheoryData<Exception> RecoverableErrors => new()
    {
        new HttpRequestException("HTTP 429"),
        new TimeoutException("Timed out"),
        new InvalidDataException("Truncated"),
        new AuthenticationFailedException("Sign in again")
    };

    [Theory]
    [MemberData(nameof(RecoverableErrors))]
    public async Task FailuresReturnOriginalAndExplicitWarning(Exception exception)
    {
        var result = await TranscriptProcessor.ProcessAsync(
            "Original", true, (_, _) => Task.FromException<string>(exception));
        Assert.Equal("Original", result.Original);
        Assert.Equal("Original", result.Text);
        Assert.Equal(exception.Message, result.CleanupError);
    }

    [Fact]
    public async Task EmptyOutputDoesNotEraseDictation()
    {
        var result = await TranscriptProcessor.ProcessAsync("Original", true, (_, _) => Task.FromResult(" "));
        Assert.Equal("Original", result.Text);
        Assert.NotNull(result.CleanupError);
    }

    [Fact]
    public async Task CancellationPreventsReturningTextForPaste()
    {
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            TranscriptProcessor.ProcessAsync("Original", true, (_, _) =>
            {
                cancellation.Cancel();
                return Task.FromResult("Must not paste");
            }, cancellation.Token));
    }

    [Fact]
    public async Task DoesNotHideUnexpectedProgrammingErrors()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            TranscriptProcessor.ProcessAsync(
                "Original", true, (_, _) => throw new InvalidOperationException("Bug")));
    }
}
