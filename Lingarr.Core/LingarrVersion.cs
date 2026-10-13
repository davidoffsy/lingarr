using System.Reflection;
using System.Runtime.InteropServices;
using Lingarr.Core.Models;

namespace Lingarr.Core;

public static class LingarrVersion
{
    public const string Name = "Lingarr";

    public const string GitHubRepository = "davidoffsy/lingarr-support-mkv";

    public static readonly string Number = GetCurrentVersion();

    public static string Platform { get; } = BuildPlatform();

    private static string BuildPlatform()
    {
        var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return $"linux/{arch}";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return $"windows/{arch}";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return $"macos/{arch}";
        }

        return $"unknown/{arch}";
    }

    private static string GetCurrentVersion()
    {
        var attribute = typeof(LingarrVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var version = attribute?.InformationalVersion ?? "0.0.0";

        var buildMetadataStart = version.IndexOf('+');
        return buildMetadataStart < 0 ? version : version[..buildMetadataStart];
    }

    public static VersionInfo CheckForUpdates(string? latestVersion = null)
    {
        return Compare(Number, latestVersion);
    }

    public static VersionInfo Compare(string currentVersion, string? latestVersion)
    {
        var isDevelopment = !TryParse(currentVersion, out var current);
        var latestDisplay = string.IsNullOrWhiteSpace(latestVersion)
            ? currentVersion
            : Normalize(latestVersion) ?? latestVersion;

        var newVersion = !isDevelopment
            && TryParse(latestDisplay, out var latest)
            && latest > current;

        return new VersionInfo
        {
            NewVersion = newVersion,
            IsDevelopment = isDevelopment,
            CurrentVersion = currentVersion,
            LatestVersion = latestDisplay
        };
    }

    public static string? Normalize(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var trimmed = version.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        return trimmed.Length == 0 ? null : trimmed;
    }

    public static bool TryParse(string? version, out Version parsed)
    {
        var normalized = Normalize(version);
        return Version.TryParse(normalized, out parsed!);
    }
}
