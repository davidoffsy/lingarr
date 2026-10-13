namespace Lingarr.Server.Interfaces.Services;

/// <summary>
/// Looks up the latest published version of this fork.
/// </summary>
public interface ILatestReleaseService
{
    /// <summary>
    /// Gets the latest stable version string (for example <c>0.1.0</c>), or null if unavailable.
    /// </summary>
    Task<string?> GetLatestVersion();
}
