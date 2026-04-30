namespace AveniaKYBPOC.Services;

public sealed class AveniaOptions
{
    public required string ApiKey { get; init; }
    public required string BaseUrl { get; init; }
    public required string PrivateKeyPem { get; init; }

    public static AveniaOptions FromEnvironment()
    {
        var apiKey = Environment.GetEnvironmentVariable("AVENIA_API_KEY");
        var baseUrl = Environment.GetEnvironmentVariable("AVENIA_BASE_URL");
        var privateKeyPem = Environment.GetEnvironmentVariable("AVENIA_PRIVATE_KEY_PEM");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Missing AVENIA_API_KEY environment variable.");
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("Missing AVENIA_BASE_URL environment variable.");
        }

        if (string.IsNullOrWhiteSpace(privateKeyPem))
        {
            throw new InvalidOperationException("Missing AVENIA_PRIVATE_KEY_PEM environment variable.");
        }

        return new AveniaOptions
        {
            ApiKey = apiKey,
            BaseUrl = baseUrl.TrimEnd('/'),
            PrivateKeyPem = privateKeyPem.Replace("\\n", "\n", StringComparison.Ordinal)
        };
    }
}
