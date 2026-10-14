using System.Text;
using System.Text.Json;
using Lingarr.Server.Interfaces.Services.Plex;
using Lingarr.Server.Models.Webhooks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace Lingarr.Server.Services.Plex;

public class PlexWebhookParser : IPlexWebhookParser
{
    public const int MaxPayloadBytes = 1 * 1024 * 1024;
    public const int MaxRequestBytes = 10 * 1024 * 1024;

    public async Task<string?> ReadPayloadJsonAsync(HttpRequest request, CancellationToken cancellationToken = default)
    {
        if (HasMultipartBoundary(request.ContentType, out var boundary))
        {
            return await ReadMultipartPayloadAsync(request, boundary, cancellationToken);
        }

        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(cancellationToken);
            var fromForm = form["payload"].ToString();
            return string.IsNullOrWhiteSpace(fromForm) ? null : fromForm;
        }

        if (request.ContentLength > MaxPayloadBytes)
        {
            return null;
        }

        return await ReadLimitedUtf8Async(request.Body, cancellationToken);
    }

    public PlexWebhookPayload? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return PlexWebhookPayloadMapper.Map(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool HasMultipartBoundary(string? contentTypeHeader, out string boundary)
    {
        boundary = string.Empty;
        if (!MediaTypeHeaderValue.TryParse(contentTypeHeader, out var contentType))
        {
            return false;
        }

        var value = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        boundary = value;
        return true;
    }

    private static async Task<string?> ReadMultipartPayloadAsync(
        HttpRequest request,
        string boundary,
        CancellationToken cancellationToken)
    {
        var reader = new MultipartReader(boundary, request.Body);
        string? payload = null;

        while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition))
            {
                await section.Body.CopyToAsync(Stream.Null, cancellationToken);
                continue;
            }

            var name = disposition.Name.Value;
            if (string.Equals(name, "payload", StringComparison.OrdinalIgnoreCase))
            {
                payload = await ReadLimitedUtf8Async(section.Body, cancellationToken);
            }
            else
            {
                await section.Body.CopyToAsync(Stream.Null, cancellationToken);
            }
        }

        return string.IsNullOrWhiteSpace(payload) ? null : payload;
    }

    private static async Task<string?> ReadLimitedUtf8Async(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > MaxPayloadBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0)
        {
            return null;
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
