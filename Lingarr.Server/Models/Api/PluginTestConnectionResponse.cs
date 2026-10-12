using Lingarr.Contracts.Models;

namespace Lingarr.Server.Models.Api;

public sealed class PluginTestConnectionResponse
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<LabelValue> Options { get; init; } = [];

    public static PluginTestConnectionResponse FromModels(ModelsResponse models)
    {
        var success = string.IsNullOrWhiteSpace(models.Message);
        var message = success
            ? models.Options.Count > 0
                ? $"Connected. Found {models.Options.Count} model(s)."
                : "Connected, but no models were returned. You can enter a model id manually."
            : models.Message ?? "Error testing connection.";

        return new PluginTestConnectionResponse
        {
            Success = success,
            Message = message,
            Options = models.Options
        };
    }
}
