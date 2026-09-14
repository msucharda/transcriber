namespace TinyTranscriber;

internal sealed record AppSettings(Uri Endpoint, string SubscriptionKey)
{
    public const string EndpointVariable = "AZURE_SPEECH_ENDPOINT";
    public const string KeyVariable = "AZURE_SPEECH_KEY";

    public static bool TryLoad(out AppSettings? settings, out string? error)
    {
        var endpointValue = Environment.GetEnvironmentVariable(EndpointVariable)?.Trim();
        var key = Environment.GetEnvironmentVariable(KeyVariable)?.Trim();

        if (string.IsNullOrWhiteSpace(endpointValue) || string.IsNullOrWhiteSpace(key))
        {
            settings = null;
            error = $"Set {EndpointVariable} and {KeyVariable}, then restart Tiny Transcriber.";
            return false;
        }

        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps)
        {
            settings = null;
            error = $"{EndpointVariable} must be an HTTPS URL.";
            return false;
        }

        settings = new AppSettings(endpoint, key);
        error = null;
        return true;
    }
}
