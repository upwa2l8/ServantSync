using ServantSync.Models;

namespace ServantSync.Services;

/// <summary>
/// Result of submitting a feature request.
/// </summary>
public enum FeatureRequestSubmitResult
{
    Success,
    RateLimited,
    HoneypotFailed,
    ValidationError,
}

/// <summary>
/// Row DTO for the SystemAdmin triage queue.
/// </summary>
public class FeatureRequestRow
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? SubmitterName { get; set; }
    public string? SubmitterEmail { get; set; }
    public string? SubmitterIp { get; set; }
    public FeatureRequestStatus Status { get; set; }
    public string? TriageNotes { get; set; }
    public string? LinkedSpec { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? TriagedUtc { get; set; }
}

public interface IFeatureRequestService
{
    /// <summary>
    /// Submit a new feature request from the public /feedback form.
    /// Validates honeypot + rate limit (3 per IP per hour).
    /// </summary>
    Task<(FeatureRequestSubmitResult Result, string? Message)> SubmitAsync(
        string title,
        string description,
        string? submitterName,
        string? submitterEmail,
        string? submitterUserId,
        string? submitterIp,
        string? honeypot);

    /// <summary>List all feature requests, newest first, for the triage queue.</summary>
    Task<IReadOnlyList<FeatureRequestRow>> ListAllAsync(CancellationToken ct = default);

    /// <summary>Update status + triage notes for a feature request.</summary>
    Task<bool> TriageAsync(
        int featureRequestId,
        FeatureRequestStatus status,
        string? triageNotes,
        string? linkedSpec,
        string triagedByUserId,
        CancellationToken ct = default);
}
