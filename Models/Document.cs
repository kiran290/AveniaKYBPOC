using AveniaKYBPOC.Enums;

namespace AveniaKYBPOC.Models;

public sealed class Document
{
    public Guid DocumentId { get; init; }
    public Guid SubAccountId { get; init; }
    public string DocumentType { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public string UploadUrl { get; init; } = string.Empty;
    public Status Status { get; set; } = Status.PendingUpload;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? UploadedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}
