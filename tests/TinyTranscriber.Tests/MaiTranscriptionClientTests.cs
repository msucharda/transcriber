using System.Net;

namespace TinyTranscriber.Tests;

public sealed class MaiTranscriptionClientTests
{
    [Fact]
    public async Task TranscribeUsesMultilingualMaiDefinition()
    {
        var audioPath = Path.GetTempFileName();

        try
        {
            await File.WriteAllBytesAsync(audioPath, [82, 73, 70, 70]);
            var handler = new CapturingHandler();
            var client = new MaiTranscriptionClient(new HttpClient(handler));
            var settings = new AppSettings(
                new Uri("https://speech.example.com"),
                "test-key");

            var transcript = await client.TranscribeAsync(audioPath, settings);

            Assert.Equal("Ahoj world.", transcript);
            Assert.Equal(
                "https://speech.example.com/speechtotext/transcriptions:transcribe?api-version=2025-10-15",
                handler.RequestUri?.ToString());
            Assert.Equal("test-key", handler.SubscriptionKey);
            Assert.Contains("\"model\":\"MAI-Transcribe-2\"", handler.RequestBody);
            Assert.Contains("\"transcribeStyle\":\"clean\"", handler.RequestBody);
            Assert.DoesNotContain("\"locales\"", handler.RequestBody);
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

        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            SubscriptionKey = request.Headers.GetValues("Ocp-Apim-Subscription-Key").Single();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{ "combinedPhrases": [{ "text": "Ahoj world." }] }""")
            };
        }
    }
}
