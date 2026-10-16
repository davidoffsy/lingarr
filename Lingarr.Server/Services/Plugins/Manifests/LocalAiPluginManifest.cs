using Lingarr.Contracts.Interfaces.Plugins;
using Lingarr.Contracts.Models;
using Lingarr.Contracts.Plugins;
using Lingarr.Core.Configuration;

namespace Lingarr.Server.Services.Plugins.Manifests;

public sealed class LocalAiPluginManifest : IPluginManifest
{
    public string Provider => "localai";

    public string DisplayName => "LocalAI / Ollama";

    public string? Description =>
        "Self-hosted or OpenAI-compatible deployments (Ollama, vLLM, OpenRouter, OpenCode Go, and similar gateways). Choose a protocol, then enter a base URL such as <code>http://ollama:11434</code> or <code>https://opencode.ai/zen/go/v1</code>. Full paths like <code>/v1/chat/completions</code> are still accepted. Use <strong>Test connection</strong> to load the model list.";

    public bool HasRequestTemplate => true;

    public bool SupportsConnectionTest => true;

    public bool SupportsModelOverride => true;

    public IReadOnlyList<PluginSettingField> Settings { get; } =
    [
        new()
        {
            Key = SettingKeys.Translation.LocalAi.Endpoint,
            Label = "Address",
            Type = PluginSettingType.Url,
            Required = true,
            Default = "http://ollama:11434",
            Description = "Base URL or full completion path. With an explicit protocol, a base URL is enough."
        },
        new()
        {
            Key = SettingKeys.Translation.LocalAi.Protocol,
            Label = "Protocol",
            Type = PluginSettingType.Dropdown,
            Required = true,
            Default = SettingKeys.Translation.LocalAi.Protocols.Auto,
            Description = "Auto keeps the old URL-suffix behaviour. Prefer OpenAI Chat Completions for OpenAI-compatible gateways.",
            Options =
            [
                new LabelValue { Label = "Auto (from URL)", Value = SettingKeys.Translation.LocalAi.Protocols.Auto },
                new LabelValue { Label = "OpenAI Chat Completions", Value = SettingKeys.Translation.LocalAi.Protocols.OpenAiChat },
                new LabelValue { Label = "Ollama Generate", Value = SettingKeys.Translation.LocalAi.Protocols.OllamaGenerate }
            ]
        },
        new()
        {
            Key = SettingKeys.Translation.LocalAi.ApiKey,
            Label = "API key (optional)",
            Type = PluginSettingType.Secret,
            Required = false,
            Description = "Bearer token if the deployment requires authentication. Stored encrypted."
        },
        new()
        {
            Key = SettingKeys.Translation.LocalAi.Model,
            Label = "AI Model",
            Type = PluginSettingType.RemoteDropdown,
            Required = true,
            AllowCustom = true,
            OptionsEndpoint = "/api/plugin/localai/models",
            Description = "Test the connection to load models, then select one or type a model id."
        }
    ];
}
