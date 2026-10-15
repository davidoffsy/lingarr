using System.Text.Json;

namespace Lingarr.Server.Services.Webhooks;

public static class ArrWebhookJsonReader
{
    public const int MaxRequestBytes = 1 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<ArrWebhookJsonRead<T>> ReadAsync<T>(
        Stream body,
        CancellationToken cancellationToken = default)
        where T : class
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await body.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > MaxRequestBytes)
            {
                return ArrWebhookJsonRead<T>.TooLarge();
            }

            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0)
        {
            return ArrWebhookJsonRead<T>.Empty();
        }

        buffer.Position = 0;
        try
        {
            var payload = await JsonSerializer.DeserializeAsync<T>(buffer, JsonOptions, cancellationToken);
            return ArrWebhookJsonRead<T>.FromPayload(payload);
        }
        catch (JsonException)
        {
            return ArrWebhookJsonRead<T>.InvalidJson();
        }
    }
}

public sealed class ArrWebhookJsonRead<T> where T : class
{
    public T? Payload { get; private init; }
    public bool IsTooLarge { get; private init; }
    public bool IsInvalidJson { get; private init; }

    public bool Ok => !IsTooLarge && !IsInvalidJson;

    public static ArrWebhookJsonRead<T> Empty() => new();

    public static ArrWebhookJsonRead<T> FromPayload(T? payload) => new() { Payload = payload };

    public static ArrWebhookJsonRead<T> TooLarge() => new() { IsTooLarge = true };

    public static ArrWebhookJsonRead<T> InvalidJson() => new() { IsInvalidJson = true };
}
