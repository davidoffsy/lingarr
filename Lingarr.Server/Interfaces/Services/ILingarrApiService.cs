using Lingarr.Server.Models.Telemetry;

namespace Lingarr.Server.Interfaces.Services;

/// <summary>
/// Service for communicating with the Lingarr API (api.lingarr.com)
/// </summary>
public interface ILingarrApiService
{
    /// <summary>
    /// Submits telemetry data to Lingarr API
    /// </summary>
    /// <param name="payload">Telemetry payload to submit</param>
    /// <returns>True if submission was successful, false otherwise</returns>
    Task<bool> SubmitTelemetry(TelemetryPayload payload);
}
