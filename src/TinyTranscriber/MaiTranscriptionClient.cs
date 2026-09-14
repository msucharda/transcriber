using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TinyTranscriber;

internal sealed class MaiTranscriptionClient(HttpClient httpClient)
{
    private const string ApiVersion = "2025-10-15";

    public async Task<string> TranscribeAsync(
        string audioPath,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        var requestUri = new Uri(
            settings.Endpoint,
            $"/speechtotext/transcriptions:transcribe?api-version={ApiVersion}");

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Add("Ocp-Apim-Subscription-Key", settings.SubscriptionKey);
        request.Content = CreateContent(audioPath);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Azure Speech returned {(int)response.StatusCode} {response.ReasonPhrase}: {responseBody}");
        }

        return TranscriptionResponseParser.Parse(responseBody);
    }

    private static MultipartFormDataContent CreateContent(string audioPath)
    {
        var definition = JsonSerializer.Serialize(new
        {
            enhancedMode = new
            {
                enabled = true,
                model = "MAI-Transcribe-2",
                modelOptions = new
                {
                    transcribeStyle = "clean",
                    timestamps = "none"
                }
            }
        });

        var content = new MultipartFormDataContent();
        var audioContent = new StreamContent(File.OpenRead(audioPath));
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audioContent, "audio", Path.GetFileName(audioPath));
        content.Add(new StringContent(definition, Encoding.UTF8, "application/json"), "definition");
        return content;
    }
}
