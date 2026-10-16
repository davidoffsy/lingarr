using System.Net.Http.Headers;
using System.Net.Http.Json;
using Lingarr.Contracts.Models;
using Lingarr.Server.Models;

namespace Lingarr.Server.Services.Translation.OpenAiCompatible;

/// <summary>
/// Fetches and reads the OpenAI-style model catalogue (<c>GET {base}/v1/models</c>).
/// Errors are reported as a message on <see cref="ModelsResponse"/> instead of throwing so
/// the settings UI can show them; an empty catalogue is still a success because some
/// gateways omit the endpoint entirely.
/// </summary>
public static class OpenAiCompatibleModelCatalog
{
    public static async Task<ModelsResponse> FetchAsync(
        HttpClient httpClient,
        string endpoint,
        string? apiKey,
        CancellationToken cancellationToken = default)
    {
        var modelsUrl = OpenAiCompatibleEndpoint.ResolveModelsUrl(endpoint);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));

            using var request = new HttpRequestMessage(HttpMethod.Get, modelsUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            OpenAiCompatibleHttp.ApplyBearer(request, apiKey);
            OpenAiCompatibleHttp.ApplyClientHeaders(request);

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

            return await ReadModelsAsync(response, timeout.Token);
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

    /// <summary>
    /// Reads an OpenAI-style <c>{ "data": [{ "id": ... }] }</c> response. Shared with the
    /// LocalAI catalogue so both providers accept the same payloads.
    /// </summary>
    public static async Task<ModelsResponse> ReadModelsAsync(
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
}
