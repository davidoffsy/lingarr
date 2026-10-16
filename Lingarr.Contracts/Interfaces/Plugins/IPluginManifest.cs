using Lingarr.Contracts.Plugins;

namespace Lingarr.Contracts.Interfaces.Plugins;

/// <summary>
/// Describes a translation provider to the settings UI so the configuration form
/// renders from the manifest without provider specific frontend code.
/// </summary>
public interface IPluginManifest
{
    string Provider { get; }
    string DisplayName { get; }
    string? Description { get; }
    IReadOnlyList<PluginSettingField> Settings { get; }
    bool HasRequestTemplate => false;
    bool SupportsConnectionTest => false;

    /// <summary>
    /// True when a chain candidate for this provider may carry its own model. Providers that declare
    /// it build one instance per candidate and honour the model override; the settings UI offers a
    /// per-row model picker for them only. Defaults to false so third-party plugins keep their
    /// single-model behaviour.
    /// </summary>
    bool SupportsModelOverride => false;
}
