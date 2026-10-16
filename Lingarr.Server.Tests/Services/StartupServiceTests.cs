using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Entities;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lingarr.Server.Tests.Services;

[Collection("StartupServiceEnv")]
public class StartupServiceTests : IDisposable
{
    private static readonly (string EnvVar, string SettingKey)[] EnvMap =
    {
        ("RADARR_URL", SettingKeys.Integration.RadarrUrl),
        ("RADARR_API_KEY", SettingKeys.Integration.RadarrApiKey),
        ("SONARR_URL", SettingKeys.Integration.SonarrUrl),
        ("SONARR_API_KEY", SettingKeys.Integration.SonarrApiKey),
        ("PLEX_URL", SettingKeys.Integration.PlexUrl),
        ("PLEX_TOKEN", SettingKeys.Integration.PlexToken),
        ("SOURCE_LANGUAGES", SettingKeys.Translation.SourceLanguages),
        ("TARGET_LANGUAGES", SettingKeys.Translation.TargetLanguages),
        ("USE_BATCH_TRANSLATION", SettingKeys.Translation.UseBatchTranslation),
        ("LIBRE_TRANSLATE_URL", SettingKeys.Translation.LibreTranslate.Url),
        ("LIBRE_TRANSLATE_API_KEY", SettingKeys.Translation.LibreTranslate.ApiKey),
        ("AI_PROMPT", SettingKeys.Translation.AiPrompt),
        ("AI_MAX_TOKENS", SettingKeys.Translation.AiMaxTokens),
        ("AI_THINKING", SettingKeys.Translation.AiThinking),
        ("OPENAI_MODEL", SettingKeys.Translation.OpenAi.Model),
        ("OPENAI_API_KEY", SettingKeys.Translation.OpenAi.ApiKey),
        ("ANTHROPIC_MODEL", SettingKeys.Translation.Anthropic.Model),
        ("ANTHROPIC_API_KEY", SettingKeys.Translation.Anthropic.ApiKey),
        ("ANTHROPIC_VERSION", SettingKeys.Translation.Anthropic.Version),
        ("LOCAL_AI_MODEL", SettingKeys.Translation.LocalAi.Model),
        ("LOCAL_AI_API_KEY", SettingKeys.Translation.LocalAi.ApiKey),
        ("LOCAL_AI_ENDPOINT", SettingKeys.Translation.LocalAi.Endpoint),
        ("LOCAL_AI_PROTOCOL", SettingKeys.Translation.LocalAi.Protocol),
        ("CUSTOM_ENDPOINT", SettingKeys.Translation.Custom.Endpoint),
        ("CUSTOM_MODEL", SettingKeys.Translation.Custom.Model),
        ("CUSTOM_API_KEY", SettingKeys.Translation.Custom.ApiKey),
        ("CUSTOM_CHAT_REQUEST_TEMPLATE", SettingKeys.Translation.Custom.ChatRequestTemplate),
        ("GEMINI_MODEL", SettingKeys.Translation.Gemini.Model),
        ("GEMINI_API_KEY", SettingKeys.Translation.Gemini.ApiKey),
        ("DEEPSEEK_MODEL", SettingKeys.Translation.DeepSeek.Model),
        ("DEEPSEEK_API_KEY", SettingKeys.Translation.DeepSeek.ApiKey),
        ("MISTRAL_MODEL", SettingKeys.Translation.Mistral.Model),
        ("MISTRAL_API_KEY", SettingKeys.Translation.Mistral.ApiKey),
        ("XAI_MODEL", SettingKeys.Translation.XAi.Model),
        ("XAI_API_KEY", SettingKeys.Translation.XAi.ApiKey),
        ("DEEPL_API_KEY", SettingKeys.Translation.DeepL.DeeplApiKey),
        ("AUTH_ENABLED", SettingKeys.Authentication.AuthEnabled),
        ("TELEMETRY_ENABLED", SettingKeys.Telemetry.TelemetryEnabled)
    };

    private const string ObjectArrayServiceType = "[{\"provider\":\"custom\",\"model\":\"model-a\"}]";

    /// <summary>
    /// A request template with placeholders: the environment mapping must store it verbatim, rendering
    /// happens per request.
    /// </summary>
    private const string CustomRequestTemplate =
        "{\"model\":\"{model}\",\"messages\":[{\"role\":\"user\",\"content\":\"{lineToTranslate}\"}]}";

    private readonly string _tempDir;

    public StartupServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lingarr-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        ClearEnv();
    }

    public void Dispose()
    {
        ClearEnv();
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
            // ignore
        }
    }

    public static IEnumerable<object[]> EnvMapData() =>
        EnvMap.Select(entry => new object[] { entry.EnvVar, entry.SettingKey });

    [Theory]
    [MemberData(nameof(EnvMapData))]
    public async Task ApplySettingsFromEnvironment_FileVariableSet_PopulatesSetting(
        string envVar,
        string settingKey)
    {
        // Arrange
        var path = Path.Combine(_tempDir, envVar);
        await File.WriteAllTextAsync(path, "from-file-value");
        Environment.SetEnvironmentVariable(envVar + "_FILE", path);

        var serviceProvider = BuildProvider();
        var startupService = new StartupService(serviceProvider, NullLogger<StartupService>.Instance);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal("from-file-value", await GetSettingValue(serviceProvider, settingKey));
    }

    [Fact]
    public async Task ApplySettingsFromEnvironment_ServiceType_NormalisesToJsonArray()
    {
        // Arrange
        var path = Path.Combine(_tempDir, "SERVICE_TYPE");
        await File.WriteAllTextAsync(path, "openai");
        Environment.SetEnvironmentVariable("SERVICE_TYPE_FILE", path);

        var serviceProvider = BuildProvider();
        await using (var seedScope = serviceProvider.CreateAsyncScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<LingarrDbContext>();
            seedDb.Settings.Add(new Setting { Key = SettingKeys.Translation.ServiceType, Value = "" });
            await seedDb.SaveChangesAsync();
        }
        var startupService = new StartupService(serviceProvider, NullLogger<StartupService>.Instance);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert: a legacy single-value env var is promoted to the canonical one-candidate chain.
        Assert.Equal(
            "[{\"provider\":\"openai\"}]",
            await GetSettingValue(serviceProvider, SettingKeys.Translation.ServiceType));
    }

    [Fact]
    public async Task StartAsync_ServiceTypeSeededAsLegacySingleValue_PromotesRowToJsonArray()
    {
        // Arrange: only the database row carries the value (no SERVICE_TYPE environment variable), so the
        // row is handled by the NormaliseServiceType database path.
        ClearServiceTypeEnvironment();
        var serviceProvider = BuildProvider();
        await SeedServiceTypeAsync(serviceProvider, "openai");
        var startupService = new StartupService(serviceProvider, NullLogger<StartupService>.Instance);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert: legacy single-value rows are promoted to the canonical chain form on startup.
        Assert.Equal(
            "[{\"provider\":\"openai\"}]",
            await GetSettingValue(serviceProvider, SettingKeys.Translation.ServiceType));
    }

    [Fact]
    public async Task StartAsync_ServiceTypeSeededAsJsonArray_IsLeftUnchanged()
    {
        // Arrange
        ClearServiceTypeEnvironment();
        var serviceProvider = BuildProvider();
        await SeedServiceTypeAsync(serviceProvider, "[\"openai\",\"deepl\"]");
        var startupService = new StartupService(serviceProvider, NullLogger<StartupService>.Instance);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert: a value that already looks like an array is neither re-parsed, re-serialised nor
        // validated (the early return on the '[' prefix keeps the configured order untouched).
        Assert.Equal(
            "[\"openai\",\"deepl\"]",
            await GetSettingValue(serviceProvider, SettingKeys.Translation.ServiceType));
    }

    [Fact]
    public async Task StartAsync_ServiceTypeSeededAsEmpty_IsLeftUnchanged()
    {
        // Arrange
        ClearServiceTypeEnvironment();
        var serviceProvider = BuildProvider();
        await SeedServiceTypeAsync(serviceProvider, string.Empty);
        var startupService = new StartupService(serviceProvider, NullLogger<StartupService>.Instance);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert: IsNullOrEmpty makes NormaliseServiceType return early, so the row keeps its empty value
        // instead of being replaced by the default provider.
        Assert.Equal(string.Empty, await GetSettingValue(serviceProvider, SettingKeys.Translation.ServiceType));
    }

    [Fact]
    public async Task StartAsync_ObjectArrayServiceType_IsLeftAsConfigured()
    {
        // Arrange
        ClearServiceTypeEnvironment();
        var serviceProvider = BuildProvider();
        await SeedServiceTypeAsync(serviceProvider, ObjectArrayServiceType);
        var startupService = new StartupService(serviceProvider, NullLogger<StartupService>.Instance);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert: the database path leaves any '['-prefixed value untouched, and the value it leaves
        // behind is a readable chain - TranslationChainResolver (used by every read entry point) reads
        // the object form with its per-candidate models, so no historical row has to be rewritten.
        Assert.Equal(
            ObjectArrayServiceType,
            await GetSettingValue(serviceProvider, SettingKeys.Translation.ServiceType));
    }

    [Fact]
    public async Task ApplySettingsFromEnvironment_ObjectArrayServiceType_IsPreserved()
    {
        // Arrange: a valid new-format chain supplied through SERVICE_TYPE.
        ClearServiceTypeEnvironment();
        Environment.SetEnvironmentVariable("SERVICE_TYPE", ObjectArrayServiceType);
        var serviceProvider = BuildProvider();
        await SeedServiceTypeAsync(serviceProvider, "[\"openai\"]");
        var startupService = new StartupService(serviceProvider, NullLogger<StartupService>.Instance);

        try
        {
            // Act
            await startupService.StartAsync(CancellationToken.None);

            // Assert: the environment path normalises through the Chain parser/serializer, so a
            // legitimate object-array chain survives with its models instead of being folded to the
            // default provider.
            Assert.Equal(
                ObjectArrayServiceType,
                await GetSettingValue(serviceProvider, SettingKeys.Translation.ServiceType));
        }
        finally
        {
            ClearServiceTypeEnvironment();
        }
    }

    [Fact]
    public async Task ApplySettingsFromEnvironment_BothVariablesSet_PrefersFile()
    {
        // Arrange
        var settingKey = SettingKeys.Translation.OpenAi.ApiKey;
        var path = Path.Combine(_tempDir, "openai_api_key");
        await File.WriteAllTextAsync(path, "from-file");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "from-env");
        Environment.SetEnvironmentVariable("OPENAI_API_KEY_FILE", path);

        var serviceProvider = BuildProvider();
        var startupService = new StartupService(serviceProvider, NullLogger<StartupService>.Instance);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal("from-file", await GetSettingValue(serviceProvider, settingKey));
    }

    [Theory]
    [InlineData("value\n", "value")]
    [InlineData("value\r\n", "value")]
    [InlineData("value", "value")]
    [InlineData("value\n\n", "value")]
    public async Task ApplySettingsFromEnvironment_FileHasTrailingNewlines_StripsThem(
        string fileContent,
        string expectedValue)
    {
        // Arrange
        var settingKey = SettingKeys.Translation.OpenAi.ApiKey;
        var path = Path.Combine(_tempDir, "openai_api_key_" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(path, fileContent);
        Environment.SetEnvironmentVariable("OPENAI_API_KEY_FILE", path);

        var serviceProvider = BuildProvider();
        var startupService = new StartupService(serviceProvider, NullLogger<StartupService>.Instance);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal(expectedValue, await GetSettingValue(serviceProvider, settingKey));
    }

    [Fact]
    public async Task ApplySettingsFromEnvironment_PlexToken_IsEncrypted()
    {
        Environment.SetEnvironmentVariable("PLEX_TOKEN", "raw-plex-token");
        var encryption = new PrefixEncryptionService();
        var serviceProvider = BuildProvider(encryption);
        var startupService = new StartupService(serviceProvider, NullLogger<StartupService>.Instance);

        await startupService.StartAsync(CancellationToken.None);

        Assert.Equal("enc:raw-plex-token", await GetSettingValue(serviceProvider, SettingKeys.Integration.PlexToken));
    }

    [Fact]
    public async Task ApplySettingsFromEnvironment_PlexUrlAndToken_MarksSettingsCompleted()
    {
        Environment.SetEnvironmentVariable("PLEX_URL", "http://plex:32400");
        Environment.SetEnvironmentVariable("PLEX_TOKEN", "raw-plex-token");
        var serviceProvider = BuildProvider();
        var startupService = new StartupService(serviceProvider, NullLogger<StartupService>.Instance);

        await startupService.StartAsync(CancellationToken.None);

        Assert.Equal("true", await GetSettingValue(serviceProvider, SettingKeys.Integration.PlexSettingsCompleted));
        Assert.Equal("Plex account", await GetSettingValue(serviceProvider, SettingKeys.Integration.PlexAccountTitle));
    }

    [Fact]
    public async Task ApplySettingsFromEnvironment_FileMissing_FallsBackToDirectVariable()
    {
        // Arrange
        var settingKey = SettingKeys.Translation.OpenAi.ApiKey;
        Environment.SetEnvironmentVariable("OPENAI_API_KEY_FILE", Path.Combine(_tempDir, "does-not-exist"));
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", "fallback-env-value");

        var serviceProvider = BuildProvider();
        var startupService = new StartupService(serviceProvider, NullLogger<StartupService>.Instance);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal("fallback-env-value", await GetSettingValue(serviceProvider, settingKey));
    }

    [Fact]
    public async Task ApplySettingsFromEnvironment_CustomVariables_PopulateCustomKeysOnly()
    {
        // Arrange: Custom is an independent provider, so its variables must land on the custom_* rows
        // and leave every local_ai_* row untouched - a deployment may configure both providers at once.
        Environment.SetEnvironmentVariable("CUSTOM_ENDPOINT", "http://gateway:8000/v1");
        Environment.SetEnvironmentVariable("CUSTOM_MODEL", "model-a");
        Environment.SetEnvironmentVariable("CUSTOM_API_KEY", "custom-key");
        Environment.SetEnvironmentVariable("CUSTOM_CHAT_REQUEST_TEMPLATE", CustomRequestTemplate);

        var serviceProvider = BuildProvider();
        var startupService = new StartupService(serviceProvider, NullLogger<StartupService>.Instance);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            "http://gateway:8000/v1",
            await GetSettingValue(serviceProvider, SettingKeys.Translation.Custom.Endpoint));
        Assert.Equal("model-a", await GetSettingValue(serviceProvider, SettingKeys.Translation.Custom.Model));
        Assert.Equal("custom-key", await GetSettingValue(serviceProvider, SettingKeys.Translation.Custom.ApiKey));
        Assert.Equal(
            CustomRequestTemplate,
            await GetSettingValue(serviceProvider, SettingKeys.Translation.Custom.ChatRequestTemplate));

        foreach (var localAiKey in EnvMap
                     .Where(entry => entry.SettingKey.StartsWith("local_ai", StringComparison.Ordinal))
                     .Select(entry => entry.SettingKey))
        {
            Assert.Equal(string.Empty, await GetSettingValue(serviceProvider, localAiKey));
        }
    }

    [Fact]
    public async Task ApplySettingsFromEnvironment_CustomApiKeyFile_IsPreferredAndTrimmed()
    {
        // Arrange: CUSTOM_API_KEY is a Docker-secret style value, so the _FILE variant wins over the
        // direct variable and the trailing newline of the file must not end up in the stored key.
        var path = Path.Combine(_tempDir, "custom_api_key");
        await File.WriteAllTextAsync(path, "from-file-key\n");
        Environment.SetEnvironmentVariable("CUSTOM_API_KEY_FILE", path);
        Environment.SetEnvironmentVariable("CUSTOM_API_KEY", "from-env-key");

        var serviceProvider = BuildProvider();
        var startupService = new StartupService(serviceProvider, NullLogger<StartupService>.Instance);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        Assert.Equal(
            "from-file-key",
            await GetSettingValue(serviceProvider, SettingKeys.Translation.Custom.ApiKey));
    }

    /// <summary>
    /// service_type is not part of <see cref="EnvMap"/>, so the environment variables that feed it are
    /// cleared explicitly: the database-path baselines must run without a SERVICE_TYPE override.
    /// </summary>
    private static void ClearServiceTypeEnvironment()
    {
        Environment.SetEnvironmentVariable("SERVICE_TYPE", null);
        Environment.SetEnvironmentVariable("SERVICE_TYPE_FILE", null);
    }

    /// <summary>
    /// Seeds (or updates) the service_type row. BuildProvider does not create this key, and updating an
    /// existing row is preferred over adding a duplicate.
    /// </summary>
    private static async Task SeedServiceTypeAsync(IServiceProvider serviceProvider, string value)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();
        var setting = await dbContext.Settings
            .FirstOrDefaultAsync(s => s.Key == SettingKeys.Translation.ServiceType);

        if (setting is null)
        {
            dbContext.Settings.Add(new Setting { Key = SettingKeys.Translation.ServiceType, Value = value });
        }
        else
        {
            setting.Value = value;
        }

        await dbContext.SaveChangesAsync();
    }

    private static void ClearEnv()
    {
        foreach (var (envVar, _) in EnvMap)
        {
            Environment.SetEnvironmentVariable(envVar, null);
            Environment.SetEnvironmentVariable(envVar + "_FILE", null);
        }
    }

    private static IServiceProvider BuildProvider(IEncryptionService? encryption = null)
    {
        var databaseName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddSingleton<IEncryptionService>(encryption ?? new PassthroughEncryptionService());
        services.AddDbContext<LingarrDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));

        var serviceProvider = services.BuildServiceProvider();

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();

        foreach (var (_, settingKey) in EnvMap)
        {
            dbContext.Settings.Add(new Setting { Key = settingKey, Value = "" });
        }

        dbContext.Settings.Add(new Setting { Key = SettingKeys.Integration.RadarrSettingsCompleted, Value = "false" });
        dbContext.Settings.Add(new Setting { Key = SettingKeys.Integration.SonarrSettingsCompleted, Value = "false" });
        dbContext.Settings.Add(new Setting { Key = SettingKeys.Integration.PlexAccountTitle, Value = "" });
        dbContext.Settings.Add(new Setting { Key = SettingKeys.Integration.PlexSettingsCompleted, Value = "false" });
        dbContext.SaveChanges();

        return serviceProvider;
    }

    private static async Task<string?> GetSettingValue(IServiceProvider serviceProvider, string key)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LingarrDbContext>();
        var setting = await dbContext.Settings.FindAsync(key);
        return setting?.Value;
    }
}

[CollectionDefinition("StartupServiceEnv", DisableParallelization = true)]
public class StartupServiceEnvCollection
{
}

internal sealed class PassthroughEncryptionService : IEncryptionService
{
    public string Encrypt(string plaintext) => plaintext;
    public string Decrypt(string ciphertext) => ciphertext;
}

internal sealed class PrefixEncryptionService : IEncryptionService
{
    public string Encrypt(string plaintext) => "enc:" + plaintext;
    public string Decrypt(string ciphertext) =>
        ciphertext.StartsWith("enc:", StringComparison.Ordinal) ? ciphertext["enc:".Length..] : ciphertext;
}
