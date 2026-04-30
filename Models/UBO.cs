using AveniaKYBPOC.Enums;

namespace AveniaKYBPOC.Models;

public sealed class UBO
{
    public Guid UboId { get; init; }
    public Guid SubAccountId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public decimal OwnershipPercentage { get; init; }
    public string Role { get; init; } = string.Empty;
    public DateOnly DateOfBirth { get; init; }
    public string Nationality { get; init; } = string.Empty;
    public string ResidentialAddress { get; init; } = string.Empty;
    public Guid IdentityDocumentId { get; init; }
    public Status Status { get; set; } = Status.Created;
    public DateTimeOffset CreatedAt { get; init; }
}
