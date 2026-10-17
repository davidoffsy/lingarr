using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Models;
using Lingarr.Contracts.Plugins;
using Lingarr.Core.Configuration;

namespace Lingarr.Server.Services.Plugins.Manifests;

/// <summary>
/// Independent custom provider for self-hosted or third-party OpenAI-compatible gateways.
/// Speaks one explicitly configured non-streaming protocol (Chat Completions, Responses, or
/// Anthropic Messages) and keeps its own <c>custom_*</c> settings, so it never shares an address,
/// key or model with LocalAI or the official Anthropic provider.
/// </summary>
public sealed class CustomPluginManifest : IPluginManifest
{
    public string Provider => "custom";

    public string DisplayName => "Custom";

    public string? Description =>
        "Your own gateway. Choose the protocol (<code>openai_chat</code>, <code>openai_responses</code>, or <code>anthropic_messages</code>), enter the base URL (for example <code>http://localhost:8000/v1</code> or a full protocol path), an optional key, and the model to use. Independent from LocalAI and official Anthropic. Use <strong>Test connection</strong> to load the model list; type a model id when the gateway has no <code>/models</code> endpoint.";

    public bool HasRequestTemplate => true;

    public bool SupportsConnectionTest => true;

    public bool SupportsModelOverride => true;

    public IReadOnlyList<PluginSettingField> Settings { get; } =
    [
        new()
        {
            Key = SettingKeys.Translation.Custom.Endpoint,
            Label = "Address",
            Type = PluginSettingType.Url,
            Required = true,
            Default = "http://localhost:8000/v1",
            Description = "Base URL or full endpoint path. The path of the selected protocol is appended when the URL has no API path yet."
        },
        new()
        {
            Key = SettingKeys.Translation.Custom.Protocol,
            Label = "Protocol",
            Type = PluginSettingType.Dropdown,
            Required = true,
            Default = SettingKeys.Translation.Custom.Protocols.OpenAiChat,
            Description =
                "Non-streaming only. Switching protocols keeps the same address, key and model, but the gateway has to serve the selected protocol and each protocol keeps its own request template (<strong>Request Settings</strong>).",
            Options =
            [
                new LabelValue
                {
                    Label = "OpenAI Chat Completions",
                    Value = SettingKeys.Translation.Custom.Protocols.OpenAiChat
                },
                new LabelValue
                {
                    Label = "OpenAI Responses",
                    Value = SettingKeys.Translation.Custom.Protocols.OpenAiResponses
                },
                new LabelValue
                {
                    Label = "Anthropic Messages",
                    Value = SettingKeys.Translation.Custom.Protocols.AnthropicMessages
                }
            ]
        },
        new()
        {
            Key = SettingKeys.Translation.Custom.AnthropicVersion,
            Label = "Anthropic version",
            Type = PluginSettingType.Text,
            Required = false,
            Default = "2023-06-01",
            Description = "Sent as the anthropic-version header when the protocol is Anthropic Messages. Ignored by the OpenAI protocols."
        },
        new()
        {
            Key = SettingKeys.Translation.Custom.ApiKey,
            Label = "API key (optional)",
            Type = PluginSettingType.Secret,
            Required = false,
            Description = "Bearer token for OpenAI Chat and Responses, or the x-api-key value for Anthropic Messages. Stored encrypted."
        },
        new()
        {
            Key = SettingKeys.Translation.Custom.Model,
            Label = "AI Model",
            Type = PluginSettingType.RemoteDropdown,
            Required = true,
            AllowCustom = true,
            OptionsEndpoint = "/api/plugin/custom/models",
            Description = "Test the connection to load models, then select one or type a model id."
        }
    ];
}
