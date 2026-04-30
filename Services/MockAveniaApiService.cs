using System.Text.Json;
using System.Text.Json.Serialization;
using AveniaKYBPOC.Enums;
using AveniaKYBPOC.Models;

namespace AveniaKYBPOC.Services;

public sealed class MockAveniaApiService
{
    private readonly Dictionary<Guid, SubAccount> _subAccounts = [];
    private readonly Dictionary<Guid, Document> _documents = [];
    private readonly Dictionary<Guid, UBO> _ubos = [];
    private readonly Dictionary<Guid, KYBAttempt> _kybAttempts = [];
    private readonly Random _random = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<SubAccount> CreateSubAccountAsync(object request)
    {
        PrintRequest("POST /v1/sub-accounts", request);
        await SimulateApiDelayAsync();

        var subAccount = new SubAccount
        {
            SubAccountId = Guid.NewGuid(),
            LegalBusinessName = "Avenia Demo Exports LLC",
            BusinessType = "Limited Liability Company",
            RegistrationNumber = "DEMO-REG-2026-001",
            TaxId = "98-7654321",
            Country = "US",
            ContactEmail = "kyb-demo@example.com",
            Status = Status.Created,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _subAccounts[subAccount.SubAccountId] = subAccount;
        PrintResponse("201 Created", subAccount);

        return subAccount;
    }

    public async Task<Document> CreateDocumentUploadSessionAsync(Guid subAccountId, string documentType, string fileName, string contentType)
    {
        EnsureSubAccountExists(subAccountId);

        var request = new
        {
            subAccountId,
            documentType,
            fileName,
            contentType,
            uploadMode = "SIGNED_URL"
        };

        PrintRequest("POST /v1/documents/upload-session", request);
        await SimulateApiDelayAsync();

        var documentId = Guid.NewGuid();
        var document = new Document
        {
            DocumentId = documentId,
            SubAccountId = subAccountId,
            DocumentType = documentType,
            FileName = fileName,
            ContentType = contentType,
            UploadUrl = $"https://mock-upload.avenia.local/documents/{documentId}?signature={Guid.NewGuid():N}",
            Status = Status.PendingUpload,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _documents[document.DocumentId] = document;
        PrintResponse("200 OK", document);

        return document;
    }

    public async Task<Document> UploadDocumentBinaryAsync(Guid documentId)
    {
        var document = GetDocument(documentId);

        PrintInfo($"Uploading '{document.FileName}' to signed URL...");
        await SimulateApiDelayAsync(350, 700);

        document.Status = Status.Uploaded;
        document.UploadedAt = DateTimeOffset.UtcNow;

        PrintResponse("PUT Upload Complete", new
        {
            document.DocumentId,
            document.FileName,
            document.Status,
            uploadedAt = document.UploadedAt
        });

        return document;
    }

    public async Task<Document> ProcessDocumentAsync(Guid documentId)
    {
        var document = GetDocument(documentId);

        PrintRequest("POST /v1/documents/process", new
        {
            document.DocumentId,
            document.DocumentType,
            currentStatus = document.Status
        });

        await SimulateApiDelayAsync(500, 1_000);

        document.Status = Status.Processed;
        document.ProcessedAt = DateTimeOffset.UtcNow;

        PrintResponse("200 OK", new
        {
            document.DocumentId,
            document.DocumentType,
            document.Status,
            processedAt = document.ProcessedAt
        });

        return document;
    }

    public async Task<UBO> CreateUboAsync(Guid subAccountId, Guid identityDocumentId)
    {
        EnsureSubAccountExists(subAccountId);
        EnsureDocumentProcessed(identityDocumentId, "UBO ID");

        var request = new
        {
            subAccountId,
            fullName = "Test User",
            ownershipPercentage = 100,
            role = "CEO",
            dateOfBirth = "1990-01-15",
            nationality = "US",
            residentialAddress = "100 Demo Street, Austin, TX 78701",
            identityDocumentId
        };

        PrintRequest("POST /v1/ubos", request);
        await SimulateApiDelayAsync();

        var ubo = new UBO
        {
            UboId = Guid.NewGuid(),
            SubAccountId = subAccountId,
            FullName = "Test User",
            OwnershipPercentage = 100,
            Role = "CEO",
            DateOfBirth = new DateOnly(1990, 1, 15),
            Nationality = "US",
            ResidentialAddress = "100 Demo Street, Austin, TX 78701",
            IdentityDocumentId = identityDocumentId,
            Status = Status.Approved,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _ubos[ubo.UboId] = ubo;
        PrintResponse("201 Created", ubo);

        return ubo;
    }

    public async Task<KYBAttempt> SubmitKybLevel1Async(
        Guid subAccountId,
        IReadOnlyCollection<Guid> documentIds,
        IReadOnlyCollection<Guid> uboIds)
    {
        EnsureSubAccountExists(subAccountId);
        EnsureDocumentsProcessed(documentIds);
        EnsureUbosExist(uboIds);

        var request = new
        {
            subAccountId,
            level = "KYB_LEVEL_1",
            businessProfile = new
            {
                industry = "International Trade",
                expectedMonthlyVolumeUsd = 250_000,
                website = "https://demo-business.example.com",
                businessAddress = "500 Market Street, San Francisco, CA 94105"
            },
            documentIds,
            uboIds
        };

        PrintRequest("POST /v1/kyb/level-1/submit", request);
        await SimulateApiDelayAsync();

        var attempt = new KYBAttempt
        {
            AttemptId = Guid.NewGuid(),
            SubAccountId = subAccountId,
            Level = "KYB_LEVEL_1",
            Status = Status.Processing,
            DocumentIds = documentIds.ToArray(),
            UboIds = uboIds.ToArray(),
            SubmittedAt = DateTimeOffset.UtcNow,
            ProviderReference = $"kyb_lv1_{Guid.NewGuid():N}"
        };

        _kybAttempts[attempt.AttemptId] = attempt;
        PrintResponse("202 Accepted", attempt);

        return attempt;
    }

    public async Task<KYBAttempt> ApproveKybAttemptAsync(Guid attemptId)
    {
        var attempt = GetKybAttempt(attemptId);

        PrintInfo("Provider review is running asynchronously...");
        await Task.Delay(_random.Next(2_000, 3_001));

        attempt.Status = Status.Approved;
        attempt.ApprovedAt = DateTimeOffset.UtcNow;

        var subAccount = _subAccounts[attempt.SubAccountId];
        subAccount.Status = Status.Approved;

        PrintResponse("Webhook: kyb.level_1.approved", new
        {
            attempt.AttemptId,
            attempt.ProviderReference,
            attempt.Status,
            attempt.ApprovedAt
        });

        return attempt;
    }

    public async Task<KYBAttempt> SubmitProofOfFinancialCapacityAsync(Guid subAccountId, Guid proofDocumentId)
    {
        EnsureSubAccountExists(subAccountId);
        EnsureDocumentProcessed(proofDocumentId, "Proof of Financial Capacity");

        var request = new
        {
            subAccountId,
            proofDocumentId,
            declaration = new
            {
                sourceOfFunds = "Operating revenue and retained earnings",
                expectedMonthlyUsdActivity = 250_000,
                attestedBy = "Test User"
            }
        };

        PrintRequest("POST /v1/kyb/proof-of-financial-capacity", request);
        await SimulateApiDelayAsync();

        var attempt = new KYBAttempt
        {
            AttemptId = Guid.NewGuid(),
            SubAccountId = subAccountId,
            Level = "PROOF_OF_FINANCIAL_CAPACITY",
            Status = Status.Processing,
            DocumentIds = [proofDocumentId],
            SubmittedAt = DateTimeOffset.UtcNow,
            ProviderReference = $"pof_{Guid.NewGuid():N}"
        };

        _kybAttempts[attempt.AttemptId] = attempt;
        PrintResponse("202 Accepted", attempt);

        await Task.Delay(_random.Next(1_000, 1_801));

        attempt.Status = Status.Approved;
        attempt.ApprovedAt = DateTimeOffset.UtcNow;

        PrintResponse("Webhook: proof_of_financial_capacity.approved", new
        {
            attempt.AttemptId,
            attempt.ProviderReference,
            attempt.Status,
            attempt.ApprovedAt
        });

        return attempt;
    }

    public async Task<KYBAttempt> SubmitKybUsdAsync(Guid subAccountId, IReadOnlyCollection<Guid> supportingDocumentIds)
    {
        EnsureSubAccountExists(subAccountId);
        EnsureDocumentsProcessed(supportingDocumentIds);

        var request = new
        {
            subAccountId,
            product = "USD_ACCOUNT",
            requestedCapabilities = new[]
            {
                "USD_COLLECTIONS",
                "USD_PAYOUTS",
                "WIRE_TRANSFERS"
            },
            supportingDocumentIds
        };

        PrintRequest("POST /v1/kyb/usd/submit", request);
        await SimulateApiDelayAsync();

        var attempt = new KYBAttempt
        {
            AttemptId = Guid.NewGuid(),
            SubAccountId = subAccountId,
            Level = "KYB_USD",
            Status = Status.Processing,
            DocumentIds = supportingDocumentIds.ToArray(),
            SubmittedAt = DateTimeOffset.UtcNow,
            ProviderReference = $"kyb_usd_{Guid.NewGuid():N}"
        };

        _kybAttempts[attempt.AttemptId] = attempt;
        PrintResponse("202 Accepted", attempt);

        PrintInfo("Running final USD capability verification...");
        await Task.Delay(_random.Next(1_500, 2_501));

        attempt.Status = Status.Approved;
        attempt.ApprovedAt = DateTimeOffset.UtcNow;

        PrintResponse("Webhook: kyb.usd.approved", new
        {
            attempt.AttemptId,
            attempt.ProviderReference,
            attempt.Status,
            attempt.ApprovedAt
        });

        return attempt;
    }

    private async Task SimulateApiDelayAsync(int minimumMilliseconds = 400, int maximumMilliseconds = 900)
    {
        await Task.Delay(_random.Next(minimumMilliseconds, maximumMilliseconds + 1));
    }

    private void EnsureSubAccountExists(Guid subAccountId)
    {
        if (!_subAccounts.ContainsKey(subAccountId))
        {
            throw new InvalidOperationException($"Sub-account '{subAccountId}' does not exist.");
        }
    }

    private Document GetDocument(Guid documentId)
    {
        if (!_documents.TryGetValue(documentId, out var document))
        {
            throw new InvalidOperationException($"Document '{documentId}' does not exist.");
        }

        return document;
    }

    private KYBAttempt GetKybAttempt(Guid attemptId)
    {
        if (!_kybAttempts.TryGetValue(attemptId, out var attempt))
        {
            throw new InvalidOperationException($"KYB attempt '{attemptId}' does not exist.");
        }

        return attempt;
    }

    private void EnsureDocumentProcessed(Guid documentId, string expectedDocumentType)
    {
        var document = GetDocument(documentId);

        if (!string.Equals(document.DocumentType, expectedDocumentType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Document '{documentId}' must be '{expectedDocumentType}', but is '{document.DocumentType}'.");
        }

        if (document.Status != Status.Processed)
        {
            throw new InvalidOperationException($"Document '{documentId}' is not processed. Current status: {document.Status}.");
        }
    }

    private void EnsureDocumentsProcessed(IEnumerable<Guid> documentIds)
    {
        foreach (var documentId in documentIds)
        {
            var document = GetDocument(documentId);

            if (document.Status != Status.Processed)
            {
                throw new InvalidOperationException($"Document '{document.DocumentType}' is not processed. Current status: {document.Status}.");
            }
        }
    }

    private void EnsureUbosExist(IEnumerable<Guid> uboIds)
    {
        foreach (var uboId in uboIds)
        {
            if (!_ubos.ContainsKey(uboId))
            {
                throw new InvalidOperationException($"UBO '{uboId}' does not exist.");
            }
        }
    }

    private static void PrintRequest(string endpoint, object payload)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"REQUEST  {endpoint}");
        Console.ResetColor();
        Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
        Console.WriteLine();
    }

    private static void PrintResponse(string statusLine, object payload)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"RESPONSE {statusLine}");
        Console.ResetColor();
        Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
        Console.WriteLine();
    }

    private static void PrintInfo(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ResetColor();
        Console.WriteLine();
    }
}
