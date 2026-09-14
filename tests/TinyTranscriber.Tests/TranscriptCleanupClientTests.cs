using Azure.Core;
using System.Net;
using System.Text.Json;

namespace TinyTranscriber.Tests;

public sealed class TranscriptCleanupClientTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("test-key")]
    public async Task SendsIsolatedTranscriptAndStrictSchema(string? key)
    {
        const string original = "Hm, pošli to Petrovi, teda Pavlovi, a přidej deployment checklist.";
        const string cleaned = "Pošli to Pavlovi a přidej deployment checklist.";
        var handler = new CapturingHandler(ResponseFor(new { text = cleaned }));
        using var httpClient = new HttpClient(handler);
        var credential = new TestCredential();
        var client = new TranscriptCleanupClient(httpClient, credential);
        var settings = new AppSettings(new Uri("https://speech.example.com"), key, "dictation-cleanup");

        Assert.Equal(cleaned, await client.CleanAsync(original, settings));
        Assert.Equal("https://speech.example.com/openai/v1/chat/completions", handler.Uri?.ToString());
        Assert.Equal(key, handler.Key);
        Assert.Equal(key is null, credential.Requested);
        Assert.Equal(key is null ? "Bearer" : null, handler.AuthenticationScheme);
        using var body = JsonDocument.Parse(handler.Body);
        var root = body.RootElement;
        Assert.Equal("dictation-cleanup", root.GetProperty("model").GetString());
        Assert.Equal("none", root.GetProperty("reasoning_effort").GetString());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.Equal(TranscriptCleanupClient.OutputTokenLimit(original), root.GetProperty("max_completion_tokens").GetInt32());
        Assert.False(root.TryGetProperty("temperature", out _));
        Assert.False(root.TryGetProperty("tools", out _));
        var messages = root.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("developer", messages[0].GetProperty("role").GetString());
        Assert.Equal(TranscriptCleanupClient.Instructions, messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal(original, messages[1].GetProperty("content").GetString());
        var schema = root.GetProperty("response_format").GetProperty("json_schema");
        Assert.True(schema.GetProperty("strict").GetBoolean());
        Assert.Equal("text", schema.GetProperty("schema").GetProperty("required")[0].GetString());
    }

    [Fact]
    public async Task HttpErrorsDoNotExposeServiceBody()
    {
        using var httpClient = new HttpClient(new CapturingHandler("private dictated text", HttpStatusCode.Forbidden));
        var client = new TranscriptCleanupClient(httpClient, new TestCredential());
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CleanAsync("Original", Settings()));
        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Contains("403", exception.Message);
        Assert.DoesNotContain("private dictated text", exception.Message);
    }

    [Fact]
    public async Task CallerCancellationIsNotConvertedToTimeout()
    {
        using var httpClient = new HttpClient(new CapturingHandler("{}"));
        var client = new TranscriptCleanupClient(httpClient, new TestCredential());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.CleanAsync("Original", Settings(), cancellation.Token));
    }

    [Fact]
    public async Task MissingDeploymentIsAnExplicitConfigurationError()
    {
        using var httpClient = new HttpClient(new CapturingHandler("{}"));
        var client = new TranscriptCleanupClient(httpClient, new TestCredential());
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.CleanAsync("Original", new AppSettings(new Uri("https://speech.example.com"), null)));
        Assert.Contains(AppSettings.CleanupDeploymentVariable, exception.Message);
    }

    [Theory]
    [InlineData("length")]
    [InlineData("content_filter")]
    [InlineData("tool_calls")]
    [InlineData(null)]
    public void RejectsIncompleteOrFilteredResponses(string? finishReason)
    {
        Assert.Throws<InvalidDataException>(() =>
            TranscriptCleanupClient.Parse(ResponseFor(new { text = "Partial text" }, finishReason)));
    }

    [Theory]
    [InlineData("""{"text":""}""")]
    [InlineData("""{"text":" \n "}""")]
    [InlineData("""{"text":null}""")]
    [InlineData("""{"text":42}""")]
    [InlineData("""{"text":"OK","explanation":"Unrequested"}""")]
    [InlineData("""{"text":"OK","text":"Duplicate"}""")]
    [InlineData("""{"wrong":"OK"}""")]
    [InlineData("[]")]
    [InlineData("Plain text instead of JSON")]
    public void RejectsInvalidOutput(string output)
    {
        Assert.Throws<InvalidDataException>(() => TranscriptCleanupClient.Parse(
            JsonSerializer.Serialize(new
            {
                choices = new[] { new { finish_reason = "stop", message = new { role = "assistant", content = output } } }
            })));
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{"choices":[]}""")]
    [InlineData("""{"choices":[{},{}]}""")]
    [InlineData("""{"choices":[null]}""")]
    [InlineData("""{"choices":[{"finish_reason":42}]}""")]
    [InlineData("""{"choices":[{"finish_reason":"stop","message":null}]}""")]
    public void RejectsMalformedEnvelopes(string response)
    {
        Assert.Throws<InvalidDataException>(() => TranscriptCleanupClient.Parse(response));
    }

    [Fact]
    public void RejectsRefusalEvenWithContent()
    {
        var response = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { finish_reason = "stop", message = new
                {
                    role = "assistant", content = """{"text":"Do not paste this"}""", refusal = "Cannot comply"
                } }
            }
        });
        Assert.Throws<InvalidDataException>(() => TranscriptCleanupClient.Parse(response));
    }

    [Fact]
    public void PreservesUnicodeQuotesCodeAndLineBreaks()
    {
        const string text = "Použij `GetUserAsync()`.\nŘekl: \"No, to je jako včera.\"";
        Assert.Equal(text, TranscriptCleanupClient.Parse(ResponseFor(new { text })));
    }

    [Theory]
    [InlineData(1, 512)]
    [InlineData(100, 512)]
    [InlineData(500, 1128)]
    [InlineData(3000, 4096)]
    public void BoundsOutputBudgetForShortAndLongDictation(int length, int expected)
    {
        Assert.Equal(expected, TranscriptCleanupClient.OutputTokenLimit(new string('a', length)));
    }

    [Fact]
    public void OutputBudgetAccountsForCzechUtf8Characters()
    {
        Assert.Equal(1328, TranscriptCleanupClient.OutputTokenLimit(new string('č', 300)));
    }

    private static AppSettings Settings() =>
        new(new Uri("https://speech.example.com"), null, "dictation-cleanup");

    private static string ResponseFor(object output, string? finishReason = "stop") =>
        JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { finish_reason = finishReason, message = new { role = "assistant", content = JsonSerializer.Serialize(output) } }
            }
        });

    private sealed class CapturingHandler(string response, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? Uri { get; private set; }
        public string? Key { get; private set; }
        public string? AuthenticationScheme { get; private set; }
        public string Body { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Uri = request.RequestUri;
            Key = request.Headers.TryGetValues("api-key", out var keys) ? keys.Single() : null;
            AuthenticationScheme = request.Headers.Authorization?.Scheme;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(response) };
        }
    }

    private sealed class TestCredential : TokenCredential
    {
        public bool Requested { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requested = true;
            Assert.Equal(["https://cognitiveservices.azure.com/.default"], requestContext.Scopes);
            return ValueTask.FromResult(new AccessToken("test-token", DateTimeOffset.UtcNow.AddHours(1)));
        }
    }
}
