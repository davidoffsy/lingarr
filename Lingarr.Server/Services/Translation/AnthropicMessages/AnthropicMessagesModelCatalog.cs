using System.Net.Http.Headers;
using Lingarr.Contracts.Models;
using Lingarr.Server.Services.Translation.OpenAiCompatible;

namespace Lingarr.Server.Services.Translation.AnthropicMessages;

/// <summary>
/// Fetches an Anthropic-style model catalogue (<c>GET {base}/v1/models</c>) with
/// <c>x-api-key</c> and <c>anthropic-version</c>. A missing catalogue is a message, not an
/// exception: the model field still accepts a typed id.
/// </summary>
public static class AnthropicMessagesModelCatalog
{
    public static async Task<ModelsResponse> FetchAsync(
        HttpClient httpClient,
        string endpoint,
        string? apiKey,
        string? version,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return new ModelsResponse
            {
                Message = "Anthropic version is not configured. Type a model id, or set the version to load the model list."
            };
        }

        var modelsUrl = OpenAiCompatibleEndpoint.ResolveModelsUrl(endpoint);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));

            using var request = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            AnthropicMessagesHeaders.Apply(request, apiKey, version);

            using var response = await httpClient.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var preview = await HttpResponsePreview.ReadAsync(response, timeout.Token);
                var suffix = string.IsNullOrEmpty(preview) ? string.Empty : $" {preview}";
                return new ModelsResponse
                {
                    Message = $"Failed to fetch models from {modelsUrl}. Status: {response.StatusCode}.{suffix}"
                };
            }

            return await OpenAiCompatibleModelCatalog.ReadModelsAsync(response, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ModelsResponse
            {
                Message = $"Timed out fetching models from {modelsUrl}."
            };
        }
        catch (Exception exception)
        {
            return new ModelsResponse
            {
                Message = $"Error fetching models from {modelsUrl}: {exception.Message}"
            };
        }
    }
}
