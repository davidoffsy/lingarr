using System.Net.Http.Headers;
using System.Net.Http.Json;
using Lingarr.Contracts.Models;
using Lingarr.Server.Models;
using Lingarr.Server.Models.Integrations.Translation;

namespace Lingarr.Server.Services.Translation.LocalAi;

public static class LocalAiModelCatalog
{
    public static async Task<ModelsResponse> FetchAsync(
        HttpClient httpClient,
        string endpoint,
        string? protocolValue,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        var protocol = LocalAiProtocolParser.Parse(protocolValue);
        var modelsUrl = LocalAiEndpointResolver.ResolveModelsUrl(endpoint, protocol);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));

            using var request = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            LocalAiHttp.ApplyBearer(request, apiKey);
            LocalAiHttp.ApplyClientHeaders(request);

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

            return protocol == LocalAiProtocol.OpenAiChat
                   || (protocol == LocalAiProtocol.Auto && LocalAiEndpointResolver.LooksLikeChatCompletions(endpoint))
                ? await ReadOpenAiModelsAsync(response, timeout.Token)
                : await ReadOllamaTagsAsync(response, timeout.Token);
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

    private static async Task<ModelsResponse> ReadOpenAiModelsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var modelsResponse = await response.Content.ReadFromJsonAsync<ModelsListResponse>(cancellationToken);
        if (modelsResponse?.Data == null)
        {
            return new ModelsResponse
            {
                Message = "No models data returned from the API."
            };
        }

        var options = modelsResponse.Data
            .Where(model => !string.IsNullOrWhiteSpace(model.Id))
            .Select(model => new LabelValue { Label = model.Id, Value = model.Id })
            .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ModelsResponse { Options = options };
    }

    private static async Task<ModelsResponse> ReadOllamaTagsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var tags = await response.Content.ReadFromJsonAsync<OllamaTagsResponse>(cancellationToken);
        if (tags?.Models == null)
        {
            return new ModelsResponse
            {
                Message = "No models data returned from the Ollama tags API."
            };
        }

        var options = tags.Models
            .Select(model => model.Name ?? model.Model)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => new LabelValue { Label = name!, Value = name! })
            .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ModelsResponse { Options = options };
    }
}
