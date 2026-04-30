using System.Text;
using System.IO.Compression;
using System.Text.Json;

namespace AveniaKYBPOC.Services;

public sealed class RealAveniaOnboardingService
{
    private readonly RealAveniaApiService _api;

    public RealAveniaOnboardingService(RealAveniaApiService api)
    {
        _api = api;
    }

    public async Task RunFullOnboardingAsync()
    {
        try
        {
            PrintStep("Step 0 - Verify Sandbox Credentials");
            await _api.GetAsync("/v2/account/account-info");
            PrintSuccess("Sandbox credentials are working.");

            var uboCpf = Environment.GetEnvironmentVariable("AVENIA_UBO_CPF") ?? GenerateBrazilCpf();
            var companyCnpj = Environment.GetEnvironmentVariable("AVENIA_COMPANY_TAX_ID") ?? GenerateBrazilCnpj();
            var formattedCompanyCnpj = FormatBrazilCnpj(companyCnpj);
            var resumeSubAccountId = Environment.GetEnvironmentVariable("AVENIA_RESUME_SUBACCOUNT_ID");
            string subAccountName;
            string subAccountId;
            string certificateDocumentId;
            string taxDocumentId;
            string uboIdentityDocumentId;
            string financialCapacityDocumentId;
            string proofOfRevenueDocumentId;

            if (string.IsNullOrWhiteSpace(resumeSubAccountId))
            {
                PrintStep("Step 1 - Create COMPANY Subaccount");
                subAccountName = $"Nexora Digital Solutions Ltd - POC {DateTime.UtcNow:yyyyMMddHHmmss}";

                var subAccountResponse = await _api.PostJsonAsync("/v2/account/sub-accounts", new
                {
                    accountType = "COMPANY",
                    name = subAccountName
                });

                subAccountId = RequiredRootString(subAccountResponse.Body, "id");
                PrintSuccess($"Subaccount created. Name: {subAccountName}; ID: {subAccountId}");

                PrintStep("Step 2 - Upload Required Documents");
                var certificatePayload = GetUploadPayload(
                    "AVENIA_CERT_FILE",
                    "certificate-of-incorporation.pdf",
                    "application/pdf",
                    CreatePdfBytes("Certificate of Incorporation", "NEXORA SOLUCOES DIGITAIS LTDA"));
                var taxPayload = GetUploadPayload(
                    "AVENIA_TAX_FILE",
                    "company-tax-identification.pdf",
                    "application/pdf",
                    CreatePdfBytes("Company Tax Identification Document", formattedCompanyCnpj));
                var uboIdPayload = GetUploadPayload(
                    "AVENIA_UBO_ID_FILE",
                    "test-user-passport.png",
                    "image/png",
                    CreatePngBytes());
                var financialCapacityPayload = GetUploadPayload(
                    "AVENIA_POFC_FILE",
                    "proof-of-financial-capacity.pdf",
                    "application/pdf",
                    CreatePdfBytes("Proof of Financial Capacity", "Sample bank statement for sandbox KYB."));
                var proofOfRevenuePayload = GetUploadPayload(
                    "AVENIA_REVENUE_FILE",
                    "proof-of-revenue.pdf",
                    "application/pdf",
                    CreatePdfBytes("Proof of Revenue", "Sample revenue report for sandbox KYB."));

                certificateDocumentId = await CreateUploadAndWaitForDocumentAsync(
                    subAccountId,
                    "CERTIFICATE-OF-INCORPORATION",
                    certificatePayload.FileName,
                    certificatePayload.ContentType,
                    certificatePayload.Bytes);

                taxDocumentId = await CreateUploadAndWaitForDocumentAsync(
                    subAccountId,
                    "COMPANY-TAX-IDENTIFICATION-DOCUMENT",
                    taxPayload.FileName,
                    taxPayload.ContentType,
                    taxPayload.Bytes);

                uboIdentityDocumentId = await CreateUploadAndWaitForDocumentAsync(
                    subAccountId,
                    Environment.GetEnvironmentVariable("AVENIA_UBO_ID_DOCUMENT_TYPE") ?? "PASSPORT",
                    uboIdPayload.FileName,
                    uboIdPayload.ContentType,
                    uboIdPayload.Bytes);

                financialCapacityDocumentId = await CreateUploadAndWaitForDocumentAsync(
                    subAccountId,
                    "PROOF-OF-FINANCIAL-CAPACITY",
                    financialCapacityPayload.FileName,
                    financialCapacityPayload.ContentType,
                    financialCapacityPayload.Bytes);

                proofOfRevenueDocumentId = await CreateUploadAndWaitForDocumentAsync(
                    subAccountId,
                    "PROOF-OF-REVENUE",
                    proofOfRevenuePayload.FileName,
                    proofOfRevenuePayload.ContentType,
                    proofOfRevenuePayload.Bytes);

                PrintSuccess("All required documents uploaded and ready.");
            }
            else
            {
                PrintStep("Step 1/2 - Resume Existing Subaccount And Ready Documents");
                subAccountName = Environment.GetEnvironmentVariable("AVENIA_RESUME_SUBACCOUNT_NAME") ?? "<existing subaccount>";
                subAccountId = resumeSubAccountId;
                certificateDocumentId = RequiredEnvironment("AVENIA_RESUME_CERT_DOC_ID");
                taxDocumentId = RequiredEnvironment("AVENIA_RESUME_TAX_DOC_ID");
                financialCapacityDocumentId = RequiredEnvironment("AVENIA_RESUME_POFC_DOC_ID");
                proofOfRevenueDocumentId = RequiredEnvironment("AVENIA_RESUME_REVENUE_DOC_ID");
                PrintSuccess($"Resuming subaccount {subAccountId} with existing ready documents.");

                if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AVENIA_UBO_ID_FILE")))
                {
                    PrintStep("Step 2b - Upload Replacement UBO ID Document");
                    var frontPayload = GetUploadPayload(
                        "AVENIA_UBO_ID_FILE",
                        "test-user-id-front.png",
                        "image/png",
                        CreatePngBytes());
                    var backPayload = GetUploadPayloadOrNull("AVENIA_UBO_ID_BACK_FILE");

                    uboIdentityDocumentId = await CreateUploadAndWaitForDocumentAsync(
                        subAccountId,
                        Environment.GetEnvironmentVariable("AVENIA_UBO_ID_DOCUMENT_TYPE") ?? "ID",
                        frontPayload.FileName,
                        frontPayload.ContentType,
                        frontPayload.Bytes,
                        backPayload);
                }
                else
                {
                    uboIdentityDocumentId = RequiredEnvironment("AVENIA_RESUME_UBO_ID_DOC_ID");
                }
            }

            var skipKybLevelOne = string.Equals(
                Environment.GetEnvironmentVariable("AVENIA_KYB_LEVEL_1_ALREADY_APPROVED"),
                "true",
                StringComparison.OrdinalIgnoreCase);

            if (!skipKybLevelOne)
            {
                var uboId = Environment.GetEnvironmentVariable("AVENIA_RESUME_UBO_ID");

                if (string.IsNullOrWhiteSpace(uboId))
                {
                    PrintStep("Step 3 - Create UBO");
                    var uboResponse = await _api.PostJsonAsync(
                        $"/v2/account/ubos?subAccountId={subAccountId}",
                        CreateUboRequest(uboIdentityDocumentId, uboCpf));

                    uboId = RequiredRootString(uboResponse.Body, "id");
                    PrintSuccess($"UBO created: {uboId}");
                }
                else
                {
                    PrintStep("Step 3 - Resume Existing UBO");
                    PrintSuccess($"Using existing UBO: {uboId}");
                }

                PrintStep("Step 4 - Submit KYB Level 1");
                var kybLevelOneResponse = await _api.PostJsonAsync($"/v2/kyc/new-level-1/api?subAccountId={subAccountId}", new
                {
                    uboIds = new[] { uboId },
                    companyLegalName = "NEXORA SOLUCOES DIGITAIS LTDA",
                    companyRegistrationNumber = companyCnpj,
                    taxIdentificationNumberTin = formattedCompanyCnpj,
                    businessActivityDescription = "Custom software development and IT consulting services. The company provides SaaS solutions for financial institutions.",
                    website = "https://www.nexoradigital.com.br",
                    businessModel = "Software house that develops solutions for financial institutions",
                    socialMedia = "https://linkedin.com/company/nexoradigital",
                    reasonForAccountOpening = "receive_payments_for_goods_and_services",
                    sourceOfFundsAndIncome = "sales_of_goods_and_services",
                    numberOfEmployees = "1-10",
                    estimatedAnnualRevenueUsd = "less_than_100k",
                    estimatedMonthlyVolumeUsd = "2000",
                    countryTaxResidence = "BRA",
                    countrySubdivisionTaxResidence = "BR-SP",
                    companyStreetLine1 = "AV PAULISTA 1000 CONJ 204",
                    companyStreetLine2 = "",
                    companyStreetLine3 = "",
                    companyCity = "SAO PAULO",
                    companyState = "SP",
                    companyZipCode = "01310-100",
                    companyCountry = "Brazil",
                    certificateOfIncorporationDocumentId = certificateDocumentId,
                    taxIdentificationDocumentId = taxDocumentId
                });

                var kybLevelOneAttemptId = RequiredRootString(kybLevelOneResponse.Body, "id");
                PrintSuccess($"KYB Level 1 submitted: {kybLevelOneAttemptId}");

                PrintStep("Step 5 - Poll KYB Level 1 Approval");
                await PollAttemptUntilApprovedAsync(
                    $"KYB Level 1 {kybLevelOneAttemptId}",
                    $"/v2/kyc/attempts/{kybLevelOneAttemptId}?subAccountId={subAccountId}");
                PrintSuccess("KYB Level 1 approved.");
            }
            else
            {
                PrintStep("Steps 3-5 - Skipped (KYB Level 1 already approved)");
                PrintSuccess("Resuming after KYB Level 1 approval.");
            }

            var skipPofc = string.Equals(
                Environment.GetEnvironmentVariable("AVENIA_POFC_ALREADY_APPROVED"),
                "true",
                StringComparison.OrdinalIgnoreCase);

            if (!skipPofc)
            {
                PrintStep("Step 6 - Submit Proof of Financial Capacity");
                var pofcResponse = await _api.PostJsonAsync($"/v2/account/proof-of-financial-capacity/api?subAccountId={subAccountId}", new
                {
                    uploadedPoFCId = financialCapacityDocumentId
                });

                var pofcAttemptId = RequiredRootString(pofcResponse.Body, "id");
                PrintSuccess($"Proof of Financial Capacity submitted: {pofcAttemptId}");

                await PollAttemptUntilApprovedAsync(
                    $"Proof of Financial Capacity {pofcAttemptId}",
                    $"/v2/account/proof-of-financial-capacity/attempts/{pofcAttemptId}?subAccountId={subAccountId}");
                PrintSuccess("Proof of Financial Capacity approved.");
            }
            else
            {
                PrintStep("Step 6 - Skipped (PoFC already approved)");
                PrintSuccess("Resuming after PoFC approval.");
            }

            PrintStep("Step 7 - Submit KYB USD");
            var kybUsdResponse = await _api.PostJsonAsync($"/v2/kyc/usd/api?subAccountId={subAccountId}", new
            {
                businessType = "llc",
                businessIndustries = new[] { "519290" },
                proofOfRevenueDocId = proofOfRevenueDocumentId,
                certificateOfIncorporationDocId = certificateDocumentId,
                proofOfFinancialCapacityDocId = financialCapacityDocumentId
            });

            var kybUsdAttemptId = RequiredRootString(kybUsdResponse.Body, "attemptId");
            PrintSuccess($"KYB USD submitted: {kybUsdAttemptId}");

            await PollAttemptUntilApprovedAsync(
                $"KYB USD {kybUsdAttemptId}",
                $"/v2/kyc/attempts/{kybUsdAttemptId}?subAccountId={subAccountId}");
            PrintSuccess("KYB USD approved.");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"FINAL SUBACCOUNT NAME: {subAccountName}");
            Console.WriteLine($"FINAL SUBACCOUNT ID: {subAccountId}");
            Console.WriteLine("FINAL KYB STATUS: APPROVED");
            Console.WriteLine("FINAL STATUS: SUCCESS");
            Console.ResetColor();
        }
        catch (Exception exception)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"ERROR: {exception.Message}");
            Console.ResetColor();
            throw;
        }
    }

    private async Task<string> CreateUploadAndWaitForDocumentAsync(
        string subAccountId,
        string documentType,
        string fileName,
        string contentType,
        byte[] bytes,
        UploadPayload? backPayload = null)
    {
        Console.WriteLine($"Document: {documentType} ({fileName})");

        var documentResponse = await _api.PostJsonAsync($"/v2/documents?subAccountId={subAccountId}", new
        {
            documentType,
            isDoubleSided = backPayload is not null
        });

        var documentId = RequiredRootString(documentResponse.Body, "id");
        var uploadUrl = RequiredRootString(documentResponse.Body, "uploadURLFront");

        await _api.PutUploadAsync(uploadUrl, bytes, contentType);

        if (backPayload is not null)
        {
            var uploadUrlBack = RequiredRootString(documentResponse.Body, "uploadURLBack");
            await _api.PutUploadAsync(uploadUrlBack, backPayload.Bytes, backPayload.ContentType);
        }

        await PollDocumentUntilReadyAsync(subAccountId, documentId);

        return documentId;
    }

    private static UploadPayload GetUploadPayload(string fileEnvironmentVariable, string fallbackFileName, string fallbackContentType, byte[] fallbackBytes)
    {
        var filePath = Environment.GetEnvironmentVariable(fileEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new UploadPayload(fallbackFileName, fallbackContentType, fallbackBytes);
        }

        if (!File.Exists(filePath))
        {
            throw new InvalidOperationException($"Configured file does not exist: {filePath}");
        }

        return new UploadPayload(
            Path.GetFileName(filePath),
            GetContentType(filePath),
            File.ReadAllBytes(filePath));
    }

    private static UploadPayload? GetUploadPayloadOrNull(string fileEnvironmentVariable)
    {
        var filePath = Environment.GetEnvironmentVariable(fileEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        if (!File.Exists(filePath))
        {
            throw new InvalidOperationException($"Configured file does not exist: {filePath}");
        }

        return new UploadPayload(
            Path.GetFileName(filePath),
            GetContentType(filePath),
            File.ReadAllBytes(filePath));
    }

    private static string GetContentType(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => throw new InvalidOperationException($"Unsupported file type for upload: {filePath}")
        };
    }

    private async Task PollDocumentUntilReadyAsync(string subAccountId, string documentId)
    {
        var requestUri = $"/v2/documents/{documentId}?subAccountId={subAccountId}";

        for (var attempt = 1; attempt <= 20; attempt++)
        {
            var response = await _api.GetAsync(requestUri);

            using var json = JsonDocument.Parse(response.Body);
            var document = json.RootElement.GetProperty("document");
            var ready = document.TryGetProperty("ready", out var readyElement) && readyElement.GetBoolean();
            var status = OptionalString(document, "uploadStatusFront") ?? "UNKNOWN";

            if (ready)
            {
                PrintSuccess($"Document ready: {documentId} ({status})");
                return;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Document {documentId} not ready yet. Status: {status}. Poll {attempt}/20.");
            Console.ResetColor();
            Console.WriteLine();
            await Task.Delay(2_000);
        }

        throw new InvalidOperationException($"Document '{documentId}' did not become ready in time.");
    }

    private async Task PollAttemptUntilApprovedAsync(string label, string requestUri)
    {
        for (var attemptNumber = 1; attemptNumber <= 30; attemptNumber++)
        {
            var response = await _api.GetAsync(requestUri);

            using var json = JsonDocument.Parse(response.Body);
            var attempt = json.RootElement.GetProperty("attempt");
            var status = OptionalString(attempt, "status") ?? "UNKNOWN";
            var result = OptionalString(attempt, "result") ?? "";
            var resultMessage = OptionalString(attempt, "resultMessage") ?? "";

            if (string.Equals(result, "APPROVED", StringComparison.OrdinalIgnoreCase))
            {
                PrintSuccess($"{label} approved.");
                return;
            }

            if (string.Equals(result, "REJECTED", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"{label} rejected: {resultMessage}");
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"{label} still processing. Status: {status}; Result: {result}. Poll {attemptNumber}/30.");
            Console.ResetColor();
            Console.WriteLine();
            await Task.Delay(3_000);
        }

        throw new InvalidOperationException($"{label} was not approved in time.");
    }

    private static string RequiredRootString(string json, string propertyName)
    {
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Expected string property '{propertyName}' in response: {json}");
        }

        return value.GetString()!;
    }

    private static string RequiredEnvironment(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing {variableName} environment variable for resume mode.");
        }

        return value;
    }

    private static object CreateUboRequest(string uboIdentityDocumentId, string brazilCpf)
    {
        var profile = Environment.GetEnvironmentVariable("AVENIA_UBO_PROFILE");
        var documentCountry = Environment.GetEnvironmentVariable("AVENIA_UBO_DOCUMENT_COUNTRY") ??
                              (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AVENIA_UBO_ID_FILE")) ? "POL" : "BRA");

        if (string.Equals(profile, "USA", StringComparison.OrdinalIgnoreCase))
        {
            return new Dictionary<string, object?>
            {
                ["fullName"] = "CARLOS MENDES FERREIRA",
                ["dateOfBirth"] = "1988-07-22",
                ["countryOfTaxId"] = "USA",
                ["taxIdNumber"] = GenerateUsTaxId(),
                ["email"] = "carlos.ferreira@nexoradigital.com",
                ["phone"] = "+12125550199",
                ["percentageOfOwnership"] = "100",
                ["hasControl"] = "CEO",
                ["uploadedIdentificationId"] = uboIdentityDocumentId,
                ["documentCountry"] = documentCountry,
                ["streetLine1"] = "100 MARKET STREET",
                ["streetLine2"] = "",
                ["streetLine3"] = "",
                ["city"] = "NEW YORK",
                ["state"] = "NY",
                ["zipCode"] = "10001",
                ["country"] = "USA"
            };
        }

        return new Dictionary<string, object?>
        {
            ["fullName"] = "CARLOS MENDES FERREIRA",
            ["dateOfBirth"] = "1988-07-22",
            ["countryOfTaxId"] = "BRA",
            ["taxIdNumber"] = brazilCpf,
            ["email"] = "carlos.ferreira@nexoradigital.com.br",
            ["phone"] = "+5511987654321",
            ["percentageOfOwnership"] = "100",
            ["hasControl"] = "CEO",
            ["uploadedIdentificationId"] = uboIdentityDocumentId,
            ["documentCountry"] = documentCountry,
            ["streetLine1"] = "RUA AURORA 456 APTO 12",
            ["streetLine2"] = "",
            ["streetLine3"] = "",
            ["city"] = "SAO PAULO",
            ["state"] = "SP",
            ["zipCode"] = "01209-001",
            ["country"] = "BRA"
        };
    }

    private static string? OptionalString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null => null,
            _ => value.ToString()
        };
    }

    private static byte[] CreatePdfBytes(string title, string body)
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {PdfEscape(title).Length + PdfEscape(body).Length + 58} >>\nstream\nBT /F1 18 Tf 72 720 Td ({PdfEscape(title)}) Tj 0 -32 Td ({PdfEscape(body)}) Tj ET\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };

        var builder = new StringBuilder();
        var offsets = new List<int> { 0 };
        builder.Append("%PDF-1.4\n");

        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(index + 1).Append(" 0 obj\n");
            builder.Append(objects[index]).Append('\n');
            builder.Append("endobj\n");
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n");
        builder.Append("0 ").Append(objects.Length + 1).Append('\n');
        builder.Append("0000000000 65535 f \n");

        foreach (var offset in offsets.Skip(1))
        {
            builder.Append(offset.ToString("D10")).Append(" 00000 n \n");
        }

        builder.Append("trailer\n");
        builder.Append("<< /Root 1 0 R /Size ").Append(objects.Length + 1).Append(" >>\n");
        builder.Append("startxref\n");
        builder.Append(xrefOffset).Append('\n');
        builder.Append("%%EOF\n");

        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static string PdfEscape(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
    }

    private static string GenerateBrazilCpf()
    {
        var digits = Enumerable.Range(0, 9)
            .Select(_ => Random.Shared.Next(0, 10))
            .ToList();

        digits.Add(CalculateCpfCheckDigit(digits, 10));
        digits.Add(CalculateCpfCheckDigit(digits, 11));

        return string.Concat(digits);
    }

    private static string GenerateUsTaxId()
    {
        return $"{Random.Shared.Next(100, 899)}{Random.Shared.Next(10, 99)}{Random.Shared.Next(1000, 9999)}";
    }

    private static int CalculateCpfCheckDigit(IReadOnlyList<int> digits, int startingWeight)
    {
        var sum = 0;

        for (var index = 0; index < startingWeight - 1; index++)
        {
            sum += digits[index] * (startingWeight - index);
        }

        var result = (sum * 10) % 11;
        return result == 10 ? 0 : result;
    }

    private static string GenerateBrazilCnpj()
    {
        var digits = Enumerable.Range(0, 8)
            .Select(_ => Random.Shared.Next(0, 10))
            .Concat([0, 0, 0, 1])
            .ToList();

        digits.Add(CalculateCnpjCheckDigit(digits, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        digits.Add(CalculateCnpjCheckDigit(digits, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));

        return string.Concat(digits);
    }

    private static int CalculateCnpjCheckDigit(IReadOnlyList<int> digits, IReadOnlyList<int> weights)
    {
        var sum = 0;

        for (var index = 0; index < weights.Count; index++)
        {
            sum += digits[index] * weights[index];
        }

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }

    private static string FormatBrazilCnpj(string cnpj)
    {
        return $"{cnpj[..2]}.{cnpj[2..5]}.{cnpj[5..8]}/{cnpj[8..12]}-{cnpj[12..]}";
    }

    private static byte[] CreatePngBytes()
    {
        const int width = 640;
        const int height = 400;
        var raw = new byte[(width * 3 + 1) * height];

        for (var y = 0; y < height; y++)
        {
            var rowStart = y * (width * 3 + 1);
            raw[rowStart] = 0;

            for (var x = 0; x < width; x++)
            {
                var pixel = rowStart + 1 + x * 3;
                raw[pixel] = 245;
                raw[pixel + 1] = 248;
                raw[pixel + 2] = 252;

                if (y < 70)
                {
                    raw[pixel] = 26;
                    raw[pixel + 1] = 62;
                    raw[pixel + 2] = 114;
                }

                if (x is > 35 and < 210 && y is > 120 and < 310)
                {
                    raw[pixel] = 190;
                    raw[pixel + 1] = 205;
                    raw[pixel + 2] = 225;
                }

                if (x is > 250 and < 590 && ((y is > 125 and < 145) || (y is > 175 and < 195) || (y is > 225 and < 245)))
                {
                    raw[pixel] = 45;
                    raw[pixel + 1] = 52;
                    raw[pixel + 2] = 65;
                }
            }
        }

        return CreatePng(width, height, raw);
    }

    private static byte[] CreatePng(int width, int height, byte[] rawRgbWithFilterBytes)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        using var header = new MemoryStream();
        WriteBigEndian(header, width);
        WriteBigEndian(header, height);
        header.WriteByte(8);
        header.WriteByte(2);
        header.WriteByte(0);
        header.WriteByte(0);
        header.WriteByte(0);
        WritePngChunk(output, "IHDR", header.ToArray());

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(rawRgbWithFilterBytes);
        }

        WritePngChunk(output, "IDAT", compressed.ToArray());
        WritePngChunk(output, "IEND", Array.Empty<byte>());

        return output.ToArray();
    }

    private static void WritePngChunk(Stream output, string type, byte[] data)
    {
        WriteBigEndian(output, data.Length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crcBytes = new byte[typeBytes.Length + data.Length];
        Buffer.BlockCopy(typeBytes, 0, crcBytes, 0, typeBytes.Length);
        Buffer.BlockCopy(data, 0, crcBytes, typeBytes.Length, data.Length);
        WriteBigEndian(output, Crc32(crcBytes));
    }

    private static void WriteBigEndian(Stream stream, int value)
    {
        stream.WriteByte((byte)((value >> 24) & 0xff));
        stream.WriteByte((byte)((value >> 16) & 0xff));
        stream.WriteByte((byte)((value >> 8) & 0xff));
        stream.WriteByte((byte)(value & 0xff));
    }

    private static void WriteBigEndian(Stream stream, uint value)
    {
        stream.WriteByte((byte)((value >> 24) & 0xff));
        stream.WriteByte((byte)((value >> 16) & 0xff));
        stream.WriteByte((byte)((value >> 8) & 0xff));
        stream.WriteByte((byte)(value & 0xff));
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xffffffffu;

        foreach (var currentByte in bytes)
        {
            crc ^= currentByte;

            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 1 ? (crc >> 1) ^ 0xedb88320u : crc >> 1;
            }
        }

        return ~crc;
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

    private sealed record UploadPayload(string FileName, string ContentType, byte[] Bytes);
}
