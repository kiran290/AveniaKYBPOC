using AveniaKYBPOC.Enums;
using AveniaKYBPOC.Models;

namespace AveniaKYBPOC.Services;

public sealed class OnboardingService
{
    private readonly MockAveniaApiService _mockAveniaApiService;

    public OnboardingService(MockAveniaApiService mockAveniaApiService)
    {
        _mockAveniaApiService = mockAveniaApiService;
    }

    public async Task RunFullOnboardingAsync()
    {
        try
        {
            PrintStep("Step 1 - Create Sub Account");
            var subAccount = await _mockAveniaApiService.CreateSubAccountAsync(new
            {
                legalBusinessName = "Avenia Demo Exports LLC",
                businessType = "Limited Liability Company",
                registrationNumber = "DEMO-REG-2026-001",
                taxId = "98-7654321",
                country = "US",
                contactEmail = "kyb-demo@example.com"
            });
            PrintSuccess($"SubAccount created: {subAccount.SubAccountId}");

            PrintStep("Step 2 - Upload Documents");
            var documents = await UploadRequiredDocumentsAsync(subAccount.SubAccountId);
            PrintSuccess("Documents uploaded and processed.");

            PrintStep("Step 3 - Create UBO");
            var uboIdentityDocument = documents.Single(document => document.DocumentType == "UBO ID");
            var ubo = await _mockAveniaApiService.CreateUboAsync(subAccount.SubAccountId, uboIdentityDocument.DocumentId);
            PrintSuccess($"UBO created: {ubo.UboId}");

            PrintStep("Step 4 - Submit KYB Level 1");
            var levelOneDocumentIds = documents
                .Where(document => document.DocumentType is "Certificate of Incorporation" or "Tax Document" or "UBO ID")
                .Select(document => document.DocumentId)
                .ToArray();

            var levelOneAttempt = await _mockAveniaApiService.SubmitKybLevel1Async(
                subAccount.SubAccountId,
                levelOneDocumentIds,
                [ubo.UboId]);
            PrintSuccess($"KYB Level 1 submitted: {levelOneAttempt.AttemptId}");

            PrintStep("Step 5 - Simulate KYB Approval");
            levelOneAttempt = await _mockAveniaApiService.ApproveKybAttemptAsync(levelOneAttempt.AttemptId);
            EnsureApproved(levelOneAttempt, "KYB Level 1");
            PrintSuccess("KYB Level 1 approved.");

            PrintStep("Step 6 - Submit Proof of Financial Capacity");
            var financialCapacityDocument = documents.Single(document => document.DocumentType == "Proof of Financial Capacity");
            var proofAttempt = await _mockAveniaApiService.SubmitProofOfFinancialCapacityAsync(
                subAccount.SubAccountId,
                financialCapacityDocument.DocumentId);
            EnsureApproved(proofAttempt, "Proof of Financial Capacity");
            PrintSuccess("Financial proof approved.");

            PrintStep("Step 7 - Submit KYB USD");
            var proofOfRevenueDocument = documents.Single(document => document.DocumentType == "Proof of Revenue");
            var usdAttempt = await _mockAveniaApiService.SubmitKybUsdAsync(
                subAccount.SubAccountId,
                [financialCapacityDocument.DocumentId, proofOfRevenueDocument.DocumentId]);
            EnsureApproved(usdAttempt, "KYB USD");
            PrintSuccess("KYB USD approved.");

            PrintFinalStatus();
        }
        catch (Exception exception)
        {
            PrintError(exception.Message);
            throw;
        }
    }

    private async Task<IReadOnlyList<Document>> UploadRequiredDocumentsAsync(Guid subAccountId)
    {
        var requiredDocuments = new[]
        {
            new DocumentSeed("Certificate of Incorporation", "certificate-of-incorporation.pdf", "application/pdf"),
            new DocumentSeed("Tax Document", "tax-document.pdf", "application/pdf"),
            new DocumentSeed("UBO ID", "test-user-passport.pdf", "application/pdf"),
            new DocumentSeed("Proof of Financial Capacity", "bank-statement-q1-2026.pdf", "application/pdf"),
            new DocumentSeed("Proof of Revenue", "revenue-report-q1-2026.pdf", "application/pdf")
        };

        var processedDocuments = new List<Document>();

        foreach (var requiredDocument in requiredDocuments)
        {
            Console.WriteLine($"Document: {requiredDocument.DocumentType}");

            var document = await _mockAveniaApiService.CreateDocumentUploadSessionAsync(
                subAccountId,
                requiredDocument.DocumentType,
                requiredDocument.FileName,
                requiredDocument.ContentType);

            document = await ExecuteWithRetryAsync(
                () => _mockAveniaApiService.UploadDocumentBinaryAsync(document.DocumentId),
                $"upload {requiredDocument.FileName}");

            document = await _mockAveniaApiService.ProcessDocumentAsync(document.DocumentId);
            processedDocuments.Add(document);
        }

        return processedDocuments;
    }

    private static async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, string operationName)
    {
        const int maxAttempts = 2;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                if (attempt == 1)
                {
                    return await operation();
                }

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Retrying {operationName} (attempt {attempt}/{maxAttempts})...");
                Console.ResetColor();
                Console.WriteLine();

                return await operation();
            }
            catch when (attempt < maxAttempts)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Transient mock API issue while trying to {operationName}.");
                Console.ResetColor();
                Console.WriteLine();
                await Task.Delay(500);
            }
        }

        throw new InvalidOperationException($"Could not complete operation: {operationName}.");
    }

    private static void EnsureApproved(KYBAttempt attempt, string stage)
    {
        if (attempt.Status != Status.Approved)
        {
            throw new InvalidOperationException($"{stage} was not approved. Current status: {attempt.Status}.");
        }
    }

    private static void PrintStep(string title)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("============================================================");
        Console.WriteLine(title);
        Console.WriteLine("============================================================");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void PrintSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"SUCCESS: {message}");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void PrintError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"ERROR: {message}");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void PrintFinalStatus()
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("FINAL KYB STATUS: APPROVED");
        Console.WriteLine("FINAL STATUS: SUCCESS ✅");
        Console.ResetColor();
    }

    private sealed record DocumentSeed(string DocumentType, string FileName, string ContentType);
}
