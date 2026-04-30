using AveniaKYBPOC.Enums;

namespace AveniaKYBPOC.Models;

public sealed class SubAccount
{
    public Guid SubAccountId { get; init; }
    public string LegalBusinessName { get; init; } = string.Empty;
    public string BusinessType { get; init; } = string.Empty;
    public string RegistrationNumber { get; init; } = string.Empty;
    public string TaxId { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
    public string ContactEmail { get; init; } = string.Empty;
    public Status Status { get; set; } = Status.Created;
    public DateTimeOffset CreatedAt { get; init; }
}
