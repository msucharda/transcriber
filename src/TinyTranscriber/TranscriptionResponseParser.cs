using System.Text.Json;

namespace TinyTranscriber;

internal static class TranscriptionResponseParser
{
    public static string Parse(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;

        var combinedText = ReadTextArray(root, "combinedPhrases");
        if (!string.IsNullOrWhiteSpace(combinedText))
        {
            return combinedText;
        }

        var phraseText = ReadTextArray(root, "phrases");
        if (!string.IsNullOrWhiteSpace(phraseText))
        {
            return phraseText;
        }

        throw new InvalidDataException("Azure Speech returned no transcript text.");
    }

    private static string ReadTextArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        return string.Join(
            " ",
            items.EnumerateArray()
                .Select(item => item.TryGetProperty("text", out var text) ? text.GetString() : null)
                .Where(text => !string.IsNullOrWhiteSpace(text)))
            .Trim();
    }
}
