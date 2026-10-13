using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lingarr.Core;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Models.Telemetry;

namespace Lingarr.Server.Services;

public class LingarrApiService : ILingarrApiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<LingarrApiService> _logger;
    private readonly string _baseUrl;

    public LingarrApiService(
        IHttpClientFactory httpClientFactory,
        ILogger<LingarrApiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _baseUrl = new UriBuilder
        {
            Scheme = Uri.UriSchemeHttps,
            Host = $"api.{LingarrVersion.Name.ToLower()}.com"
        }.Uri.ToString().TrimEnd('/');
    }

    public async Task<bool> SubmitTelemetry(TelemetryPayload payload)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var signature = GenerateHmac(json);
            var httpClient = _httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            request.Headers.Add("X-Signature", signature);
            var response = await httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }
            _logger.LogWarning("Telemetry submission failed: {Status} - {Response}",
                response.StatusCode,
                await response.Content.ReadAsStringAsync());
            return false;

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit telemetry to Lingarr API");
            return false;
        }
    }

    private string GenerateHmac(string payload)
    {
        using var hmac = new HMACSHA256("tSBTCU4Qv76so0c2U8bBX0faSzc3uc6Z"u8.ToArray());
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
