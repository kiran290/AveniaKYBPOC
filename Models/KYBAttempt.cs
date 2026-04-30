using AveniaKYBPOC.Enums;

namespace AveniaKYBPOC.Models;

public sealed class KYBAttempt
{
    public Guid AttemptId { get; init; }
    public Guid SubAccountId { get; init; }
    public string Level { get; init; } = string.Empty;
    public Status Status { get; set; } = Status.Processing;
    public IReadOnlyList<Guid> DocumentIds { get; init; } = Array.Empty<Guid>();
    public IReadOnlyList<Guid> UboIds { get; init; } = Array.Empty<Guid>();
    public DateTimeOffset SubmittedAt { get; init; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string ProviderReference { get; init; } = string.Empty;
}
