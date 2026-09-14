using Azure.Core;
using System.Net;

namespace TinyTranscriber.Tests;

public sealed class MaiTranscriptionClientTests
{
    [Fact]
    public async Task TranscribeUsesEntraAndMultilingualMaiDefinition()
    {
        var audioPath = Path.GetTempFileName();

        try
        {
            await File.WriteAllBytesAsync(audioPath, [82, 73, 70, 70]);
            var handler = new CapturingHandler();
            var credential = new RecordingTokenCredential();
            var client = new MaiTranscriptionClient(new HttpClient(handler), credential);
            var settings = new AppSettings(
                new Uri("https://speech.example.com"),
                null);

            var transcript = await client.TranscribeAsync(audioPath, settings);

            Assert.Equal("Ahoj world.", transcript);
            Assert.Equal(
                "https://speech.example.com/speechtotext/transcriptions:transcribe?api-version=2025-10-15",
                handler.RequestUri?.ToString());
            Assert.Equal("Bearer test-token", handler.Authorization);
            Assert.NotNull(credential.RequestedScopes);
            Assert.Equal(
                ["https://cognitiveservices.azure.com/.default"],
                credential.RequestedScopes);
            Assert.Contains("\"model\":\"MAI-Transcribe-2\"", handler.RequestBody);
            Assert.Contains("\"transcribeStyle\":\"clean\"", handler.RequestBody);
            Assert.DoesNotContain("\"locales\"", handler.RequestBody);
        }
        finally
        {
            File.Delete(audioPath);
        }
    }

    [Fact]
    public async Task TranscribeUsesConfiguredSubscriptionKeyAsFallback()
    {
        var audioPath = Path.GetTempFileName();

        try
        {
            await File.WriteAllBytesAsync(audioPath, [82, 73, 70, 70]);
            var handler = new CapturingHandler();
            var credential = new RecordingTokenCredential();
            var client = new MaiTranscriptionClient(new HttpClient(handler), credential);
            var settings = new AppSettings(
                new Uri("https://speech.example.com"),
                "test-key");

            await client.TranscribeAsync(audioPath, settings);

            Assert.Equal("test-key", handler.SubscriptionKey);
            Assert.Null(credential.RequestedScopes);
        }
        finally
        {
            File.Delete(audioPath);
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? SubscriptionKey { get; private set; }

        public string? Authorization { get; private set; }

        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            SubscriptionKey = request.Headers.TryGetValues(
                "Ocp-Apim-Subscription-Key",
                out var keyValues)
                ? keyValues.Single()
                : null;
            Authorization = request.Headers.Authorization?.ToString();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{ "combinedPhrases": [{ "text": "Ahoj world." }] }""")
            };
        }
    }

    private sealed class RecordingTokenCredential : TokenCredential
    {
        public string[]? RequestedScopes { get; private set; }

        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken)
        {
            RequestedScopes = requestContext.Scopes;
            return CreateToken();
        }

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken)
        {
            RequestedScopes = requestContext.Scopes;
            return ValueTask.FromResult(CreateToken());
        }

        private static AccessToken CreateToken()
        {
            return new AccessToken("test-token", DateTimeOffset.UtcNow.AddHours(1));
        }
    }
}
