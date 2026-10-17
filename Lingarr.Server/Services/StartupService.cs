using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services.Plugins;
using Lingarr.Server.Services.Translation;
using Lingarr.Server.Services.Translation.Chain;
using Microsoft.EntityFrameworkCore;

namespace Lingarr.Server.Services;

public class StartupService : IHostedService
{
    private static readonly HashSet<string> EncryptedEnvironmentKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        SettingKeys.Integration.PlexToken
    };

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StartupService> _logger;

    public StartupService(IServiceProvider serviceProvider, ILogger<StartupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Initializes the application by validating and updating integration settings for integration services.
    /// This method is part of the application startup process and ensures all required service
    /// configurations are properly set.
    /// </summary>
    /// <param name="cancellationToken">Allows for cancellation of the startup process.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();

        await NormaliseServiceType(dbContext);
        var encryptionService = scope.ServiceProvider.GetRequiredService<IEncryptionService>();
        await ApplySettingsFromEnvironment(dbContext, encryptionService);
        await CompletePlexSettingsFromEnvironment(dbContext);

        var pluginRegistry = scope.ServiceProvider.GetService<IPluginRegistry>();
        var pluginLoader = scope.ServiceProvider.GetService<PluginLoader>();
        if (pluginRegistry is not null)
        {
            await SyncPluginSettings(dbContext, pluginRegistry, pluginLoader?.LoadingEnabled ?? false);
        }

        await CheckAndUpdateIntegrationSettings(dbContext, "radarr", [
            SettingKeys.Integration.RadarrUrl,
            SettingKeys.Integration.RadarrApiKey
        ]);

        await CheckAndUpdateIntegrationSettings(dbContext, "sonarr", [
            SettingKeys.Integration.SonarrUrl,
            SettingKeys.Integration.SonarrApiKey
        ]);
    }

    /// <summary>
    /// Synchronizes plugin-declared settings with the database, creating missing rows and
    /// removing rows for plugins that are no longer loaded.
    /// </summary>
    private async Task SyncPluginSettings(
        LingarrDbContext dbContext,
        IPluginRegistry pluginRegistry,
        bool pluginLoadingEnabled)
    {
        var pluginEntries = pluginRegistry.All
            .Where(plugin => !plugin.IsBuiltIn)
            .ToList();

        await AddDeclaredSettings(dbContext, pluginEntries);

        if (pluginLoadingEnabled)
        {
            await RemoveOrphanedSettings(dbContext, pluginEntries);
        }
    }

    private async Task AddDeclaredSettings(LingarrDbContext dbContext, IReadOnlyList<RegisteredPlugin> pluginEntries)
    {
        var declaredKeys = pluginEntries
            .SelectMany(plugin => plugin.Manifest.Settings.Select(field => field.Key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingByKey = await dbContext.Settings
            .Where(setting => declaredKeys.Contains(setting.Key))
            .ToDictionaryAsync(
                setting => setting.Key,
                StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in pluginEntries)
        {
            var provider = plugin.Manifest.Provider;
            foreach (var field in plugin.Manifest.Settings)
            {
                if (!existingByKey.TryGetValue(field.Key, out var existing))
                {
                    dbContext.Settings.Add(new Setting
                    {
                        Key = field.Key,
                        Value = field.Default ?? string.Empty,
                        Provider = provider
                    });
                    _logger.LogInformation("Added plugin setting {Key} for {Provider}.", field.Key, provider);
                    continue;
                }

                if (existing.Provider is null)
                {
                    _logger.LogWarning(
                        "Plugin {Provider} declares key {Key} but a built-in setting with that key already exists; skipping.",
                        provider,
                        field.Key);
                    continue;
                }

                if (!string.Equals(existing.Provider, provider, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "Plugin {Provider} declares key {Key} but it is already owned by {Owner}; skipping.",
                        provider,
                        field.Key,
                        existing.Provider);
                }
            }
        }

        await dbContext.SaveChangesAsync();
    }

    private async Task RemoveOrphanedSettings(LingarrDbContext dbContext, IReadOnlyList<RegisteredPlugin> pluginEntries)
    {
        var loadedProviders = new HashSet<string>(
            pluginEntries.Select(plugin => plugin.Manifest.Provider),
            StringComparer.OrdinalIgnoreCase);

        var ownedRows = await dbContext.Settings
            .Where(setting => setting.Provider != null)
            .ToListAsync();

        var orphaned = ownedRows
            .Where(setting => !loadedProviders.Contains(setting.Provider!))
            .ToList();

        if (orphaned.Count == 0)
        {
            return;
        }

        dbContext.Settings.RemoveRange(orphaned);
        await dbContext.SaveChangesAsync();

        foreach (var setting in orphaned)
        {
            _logger.LogInformation(
                "Removed orphaned setting {Key} owned by {Owner} (plugin not loaded).",
                setting.Key,
                setting.Provider);
        }
    }

    /// <summary>
    /// Ensures the service_type setting is stored in the canonical chain form. The setting historically
    /// held a single service identifier; on every startup we promote legacy single-value rows (existing
    /// installs and fresh seeds alike) to the ordered candidate form used by the rest of the system.
    /// Values that are already chains are left untouched: existing arrays and object arrays stay as
    /// configured and are read through <see cref="TranslationChainResolver"/>, so no historical value
    /// has to be rewritten.
    /// </summary>
    /// <param name="dbContext">The database context used to access and update settings.</param>
    private async Task NormaliseServiceType(LingarrDbContext dbContext)
    {
        var setting = await dbContext.Settings
            .FirstOrDefaultAsync(s => s.Key == SettingKeys.Translation.ServiceType);

        if (setting == null || string.IsNullOrEmpty(setting.Value))
        {
            return;
        }

        if (setting.Value.TrimStart().StartsWith('['))
        {
            return;
        }

        setting.Value = TranslationChainResolver.Normalise(setting.Value, _logger);
        await dbContext.SaveChangesAsync();

        _logger.LogInformation("Normalised service_type setting to the translation chain form.");
    }

    /// <summary>
    /// Validates and updates completion status for integration settings of a specific service.
    /// </summary>
    /// <param name="dbContext">The database context for accessing settings.</param>
    /// <param name="serviceName">Name of the service being validated (e.g., "radarr", "sonarr").</param>
    /// <param name="requiredKeys">Array of setting keys that must be present and non-empty for the service.</param>
    private async Task CheckAndUpdateIntegrationSettings(LingarrDbContext dbContext, string serviceName, string[] requiredKeys)
    {
        var completedKey = serviceName == "radarr"
            ? SettingKeys.Integration.RadarrSettingsCompleted
            : SettingKeys.Integration.SonarrSettingsCompleted;

        var settings = await dbContext.Settings
            .Where(s => requiredKeys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value);

        var allRequiredKeysHaveValues = requiredKeys.All(key =>
            settings.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value));

        if (allRequiredKeysHaveValues)
        {
            var setting = await dbContext.Settings.FindAsync(completedKey);
            if (setting != null)
            {
                setting.Value = "true";
                await dbContext.SaveChangesAsync();
                _logger.LogInformation($"{serviceName} settings completed.");
            }
        }
    }

    /// <summary>
    /// Applies settings from environment variables to the database.
    /// </summary>
    private async Task ApplySettingsFromEnvironment(LingarrDbContext dbContext, IEncryptionService encryptionService)
    {
        var environmentSettings = new Dictionary<string, string>
        {
            { "RADARR_URL", SettingKeys.Integration.RadarrUrl },
            { "RADARR_API_KEY", SettingKeys.Integration.RadarrApiKey },
            { "SONARR_URL", SettingKeys.Integration.SonarrUrl },
            { "SONARR_API_KEY", SettingKeys.Integration.SonarrApiKey },
            { "PLEX_URL", SettingKeys.Integration.PlexUrl },
            { "PLEX_TOKEN", SettingKeys.Integration.PlexToken },
            { "SOURCE_LANGUAGES", SettingKeys.Translation.SourceLanguages },
            { "TARGET_LANGUAGES", SettingKeys.Translation.TargetLanguages },

            { "SERVICE_TYPE", SettingKeys.Translation.ServiceType },
            { "USE_BATCH_TRANSLATION", SettingKeys.Translation.UseBatchTranslation },
            { "EXTRACT_TIMEOUT_MINUTES", SettingKeys.Translation.ExtractTimeoutMinutes },
            { "LIBRE_TRANSLATE_URL", SettingKeys.Translation.LibreTranslate.Url },
            { "LIBRE_TRANSLATE_API_KEY", SettingKeys.Translation.LibreTranslate.ApiKey },
            { "AI_PROMPT", SettingKeys.Translation.AiPrompt },
            { "AI_USER_PROMPT", SettingKeys.Translation.AiUserPrompt },
            { "AI_MAX_TOKENS", SettingKeys.Translation.AiMaxTokens },
            { "AI_THINKING", SettingKeys.Translation.AiThinking },
            { "PROOFREAD_PROMPT", SettingKeys.Translation.ProofreadPrompt },
            { "PROOFREAD_USER_PROMPT", SettingKeys.Translation.ProofreadUserPrompt },

            { "OPENAI_MODEL", SettingKeys.Translation.OpenAi.Model },
            { "OPENAI_API_KEY", SettingKeys.Translation.OpenAi.ApiKey },

            { "ANTHROPIC_MODEL", SettingKeys.Translation.Anthropic.Model },
            { "ANTHROPIC_API_KEY", SettingKeys.Translation.Anthropic.ApiKey },
            { "ANTHROPIC_VERSION", SettingKeys.Translation.Anthropic.Version },

            { "LOCAL_AI_MODEL", SettingKeys.Translation.LocalAi.Model },
            { "LOCAL_AI_API_KEY", SettingKeys.Translation.LocalAi.ApiKey },
            { "LOCAL_AI_ENDPOINT", SettingKeys.Translation.LocalAi.Endpoint },
            { "LOCAL_AI_PROTOCOL", SettingKeys.Translation.LocalAi.Protocol },

            // Custom deliberately keeps its own CUSTOM_* names instead of reusing LOCAL_AI_*: the two
            // providers can be configured side by side in one translation chain, so an environment
            // variable for one must never overwrite the address, key or model of the other.
            { "CUSTOM_ENDPOINT", SettingKeys.Translation.Custom.Endpoint },
            { "CUSTOM_MODEL", SettingKeys.Translation.Custom.Model },
            { "CUSTOM_API_KEY", SettingKeys.Translation.Custom.ApiKey },
            { "CUSTOM_PROTOCOL", SettingKeys.Translation.Custom.Protocol },
            { "CUSTOM_CHAT_REQUEST_TEMPLATE", SettingKeys.Translation.Custom.ChatRequestTemplate },
            { "CUSTOM_RESPONSES_REQUEST_TEMPLATE", SettingKeys.Translation.Custom.ResponsesRequestTemplate },
            { "CUSTOM_MESSAGES_REQUEST_TEMPLATE", SettingKeys.Translation.Custom.MessagesRequestTemplate },
            { "CUSTOM_ANTHROPIC_VERSION", SettingKeys.Translation.Custom.AnthropicVersion },

            { "GEMINI_MODEL", SettingKeys.Translation.Gemini.Model },
            { "GEMINI_API_KEY", SettingKeys.Translation.Gemini.ApiKey },

            { "DEEPSEEK_MODEL", SettingKeys.Translation.DeepSeek.Model },
            { "DEEPSEEK_API_KEY", SettingKeys.Translation.DeepSeek.ApiKey },

            { "MISTRAL_MODEL", SettingKeys.Translation.Mistral.Model },
            { "MISTRAL_API_KEY", SettingKeys.Translation.Mistral.ApiKey },

            { "XAI_MODEL", SettingKeys.Translation.XAi.Model },
            { "XAI_API_KEY", SettingKeys.Translation.XAi.ApiKey },

            { "DEEPL_API_KEY", SettingKeys.Translation.DeepL.DeeplApiKey },

            { "AUTH_ENABLED", SettingKeys.Authentication.AuthEnabled },
            { "TELEMETRY_ENABLED", SettingKeys.Telemetry.TelemetryEnabled }
        };

        foreach (var (envVar, settingKey) in environmentSettings)
        {
            var value = ReadFromFileEnv(envVar + "_FILE") ?? Environment.GetEnvironmentVariable(envVar);
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }
            
            if (settingKey == SettingKeys.Translation.ServiceType)
            {
                value = TranslationChainResolver.Normalise(value, _logger);
            }

            var setting = await dbContext.Settings.FirstOrDefaultAsync(s => s.Key == settingKey);
            if (setting != null)
            {
                setting.Value = EncryptedEnvironmentKeys.Contains(settingKey)
                    ? encryptionService.Encrypt(value)
                    : value;
                await dbContext.SaveChangesAsync();
            }

            _logger.LogInformation($"Updated setting '{settingKey}' from environment variable '{envVar}'.");
        }
    }

    private async Task CompletePlexSettingsFromEnvironment(LingarrDbContext dbContext)
    {
        var url = await dbContext.Settings.FindAsync(SettingKeys.Integration.PlexUrl);
        var token = await dbContext.Settings.FindAsync(SettingKeys.Integration.PlexToken);
        if (string.IsNullOrWhiteSpace(url?.Value) || string.IsNullOrWhiteSpace(token?.Value))
        {
            return;
        }

        var completed = await dbContext.Settings.FindAsync(SettingKeys.Integration.PlexSettingsCompleted);
        if (completed != null)
        {
            completed.Value = "true";
        }

        var title = await dbContext.Settings.FindAsync(SettingKeys.Integration.PlexAccountTitle);
        if (title != null && string.IsNullOrWhiteSpace(title.Value))
        {
            title.Value = "Plex account";
        }

        await dbContext.SaveChangesAsync();
        _logger.LogInformation("Plex settings completed from environment.");
    }

    private static string? ReadFromFileEnv(string fileEnvVar)
    {
        var path = Environment.GetEnvironmentVariable(fileEnvVar);
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(path).TrimEnd('\r', '\n');
        }
        catch
        {
            return null;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
