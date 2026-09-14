using Azure.Core;
using System.Net.Http.Headers;

namespace TinyTranscriber;

internal static class AzureAuthentication
{
    private static readonly string[] Scopes = ["https://cognitiveservices.azure.com/.default"];

    public static async Task AddAsync(
        HttpRequestMessage request,
        AppSettings settings,
        string keyHeader,
        TokenCredential credential,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(settings.SubscriptionKey))
        {
            request.Headers.Add(keyHeader, settings.SubscriptionKey);
            return;
        }

        var token = await credential.GetTokenAsync(new TokenRequestContext(Scopes), cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
    }
}
