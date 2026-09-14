namespace TinyTranscriber;

internal sealed record AppSettings(
    Uri Endpoint,
    string? SubscriptionKey,
    string? CleanupDeployment = null)
{
    public const string EndpointVariable = "AZURE_SPEECH_ENDPOINT";
    public const string KeyVariable = "AZURE_SPEECH_KEY";
    public const string CleanupDeploymentVariable = "TINY_TRANSCRIBER_CLEANUP_DEPLOYMENT";

    public static bool TryLoad(out AppSettings? settings, out string? error)
    {
        var endpointValue = Environment.GetEnvironmentVariable(EndpointVariable)?.Trim();
        var key = Environment.GetEnvironmentVariable(KeyVariable)?.Trim();
        var cleanupDeployment = Environment.GetEnvironmentVariable(CleanupDeploymentVariable)?.Trim();

        if (string.IsNullOrWhiteSpace(endpointValue))
        {
            settings = null;
            error = $"Set {EndpointVariable}, then restart Tiny Transcriber.";
            return false;
        }

        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps)
        {
            settings = null;
            error = $"{EndpointVariable} must be an HTTPS URL.";
            return false;
        }

        settings = new AppSettings(
            endpoint,
            string.IsNullOrWhiteSpace(key) ? null : key,
            string.IsNullOrWhiteSpace(cleanupDeployment) ? null : cleanupDeployment);
        error = null;
        return true;
    }
}
