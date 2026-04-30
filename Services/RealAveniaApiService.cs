using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AveniaKYBPOC.Services;

public sealed class RealAveniaApiService
{
    private readonly HttpClient _httpClient;
    private readonly AveniaOptions _options;
    private readonly RSA _rsa;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public RealAveniaApiService(HttpClient httpClient, AveniaOptions options)
    {
        _httpClient = httpClient;
        _options = options;
        _rsa = RSA.Create();
        _rsa.ImportFromPem(options.PrivateKeyPem);
    }

    public Task<AveniaApiResponse> GetAsync(string requestUri)
    {
        return SendSignedAsync(HttpMethod.Get, requestUri, body: null);
    }

    public Task<AveniaApiResponse> PostJsonAsync(string requestUri, object body)
    {
        var bodyJson = JsonSerializer.Serialize(body, JsonOptions);
        return SendSignedAsync(HttpMethod.Post, requestUri, bodyJson);
    }

    public async Task<AveniaApiResponse> PutUploadAsync(string uploadUrl, byte[] bytes, string contentType)
    {
        PrintUploadRequest(uploadUrl, bytes.Length, contentType);

        using var request = new HttpRequestMessage(HttpMethod.Put, uploadUrl);
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        using var response = await _httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();
        var apiResponse = new AveniaApiResponse((int)response.StatusCode, response.ReasonPhrase ?? "", responseBody);
        PrintResponse(apiResponse);
        apiResponse.EnsureSuccess();

        return apiResponse;
    }

    private async Task<AveniaApiResponse> SendSignedAsync(HttpMethod method, string requestUri, string? body)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var httpMethod = method.Method.ToUpperInvariant();
        var stringToSign = timestamp + httpMethod + requestUri + (body ?? string.Empty);
        var signature = CreateSignature(stringToSign);

        PrintRequest(httpMethod, requestUri, body);

        using var request = new HttpRequestMessage(method, _options.BaseUrl + requestUri);
        request.Headers.Add("X-API-Key", _options.ApiKey);
        request.Headers.Add("X-API-Timestamp", timestamp);
        request.Headers.Add("X-API-Signature", signature);

        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();
        var apiResponse = new AveniaApiResponse((int)response.StatusCode, response.ReasonPhrase ?? "", responseBody);
        PrintResponse(apiResponse);
        apiResponse.EnsureSuccess();

        return apiResponse;
    }

    private string CreateSignature(string stringToSign)
    {
        var signatureBytes = _rsa.SignData(
            Encoding.UTF8.GetBytes(stringToSign),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return Convert.ToBase64String(signatureBytes);
    }

    private static void PrintRequest(string method, string requestUri, string? body)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"REQUEST  {method} {requestUri}");
        Console.ResetColor();
        Console.WriteLine("Headers: X-API-Key=<set>, X-API-Timestamp=<set>, X-API-Signature=<set>");

        if (!string.IsNullOrWhiteSpace(body))
        {
            Console.WriteLine(PrettyPrintJson(body));
        }

        Console.WriteLine();
    }

    private static void PrintUploadRequest(string uploadUrl, int byteCount, string contentType)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("REQUEST  PUT <pre-signed upload URL>");
        Console.ResetColor();
        Console.WriteLine($"Content-Type: {contentType}");
        Console.WriteLine("If-None-Match: *");
        Console.WriteLine($"Bytes: {byteCount}");
        Console.WriteLine($"Upload URL host: {new Uri(uploadUrl).Host}");
        Console.WriteLine();
    }

    private static void PrintResponse(AveniaApiResponse response)
    {
        var color = response.IsSuccess ? ConsoleColor.Green : ConsoleColor.Red;
        Console.ForegroundColor = color;
        Console.WriteLine($"RESPONSE {response.StatusCode} {response.ReasonPhrase}");
        Console.ResetColor();

        if (!string.IsNullOrWhiteSpace(response.Body))
        {
            Console.WriteLine(PrettyPrintJson(response.Body));
        }

        Console.WriteLine();
    }

    private static string PrettyPrintJson(string value)
    {
        try
        {
            var node = JsonNode.Parse(value);
            RedactSensitiveJsonValues(node);
            return node?.ToJsonString(PrettyJsonOptions) ?? value;
        }
        catch (JsonException)
        {
            return value;
        }
    }

    private static void RedactSensitiveJsonValues(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var property in jsonObject.ToArray())
                {
                    if (property.Value is JsonValue jsonValue &&
                        jsonValue.TryGetValue<string>(out var stringValue) &&
                        IsSensitiveUrl(property.Key, stringValue))
                    {
                        jsonObject[property.Key] = "<pre-signed upload URL redacted>";
                    }
                    else
                    {
                        RedactSensitiveJsonValues(property.Value);
                    }
                }

                break;

            case JsonArray jsonArray:
                foreach (var child in jsonArray)
                {
                    RedactSensitiveJsonValues(child);
                }

                break;
        }
    }

    private static bool IsSensitiveUrl(string propertyName, string value)
    {
        return propertyName.Contains("uploadURL", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("X-Amz-Signature", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("X-Amz-Credential", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record AveniaApiResponse(int StatusCode, string ReasonPhrase, string Body)
{
    public bool IsSuccess => StatusCode is >= 200 and <= 299;

    public JsonDocument Json => JsonDocument.Parse(Body);

    public void EnsureSuccess()
    {
        if (!IsSuccess)
        {
            throw new InvalidOperationException($"Avenia API request failed with HTTP {StatusCode}: {Body}");
        }
    }
}
