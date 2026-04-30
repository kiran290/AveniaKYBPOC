using AveniaKYBPOC.Services;

Console.Title = "Avenia-like KYB Onboarding Demo";

Console.ForegroundColor = ConsoleColor.White;
Console.WriteLine("Avenia-like KYB Onboarding Demo (.NET 8)");
Console.ResetColor();
Console.WriteLine();

var runRealSandbox = args.Any(argument => string.Equals(argument, "--real", StringComparison.OrdinalIgnoreCase));
var accountInfoOnly = args.Any(argument => string.Equals(argument, "--account-info", StringComparison.OrdinalIgnoreCase));
var createSubAccountInfo = args.Any(argument => string.Equals(argument, "--create-subaccount-info", StringComparison.OrdinalIgnoreCase));

if (runRealSandbox)
{
    Console.WriteLine("Mode: REAL Avenia sandbox APIs");
    Console.WriteLine();

    var options = AveniaOptions.FromEnvironment();
    using var httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    var realAveniaApiService = new RealAveniaApiService(httpClient, options);

    if (createSubAccountInfo)
    {
        var subAccountName = $"Nexora Digital Solutions Ltd - BRL {DateTime.UtcNow:yyyyMMddHHmmss}";

        var subAccountResponse = await realAveniaApiService.PostJsonAsync("/v2/account/sub-accounts", new
        {
            accountType = "COMPANY",
            name = subAccountName
        });

        using var subAccountJson = subAccountResponse.Json;
        var subAccountId = subAccountJson.RootElement.GetProperty("id").GetString();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Created subaccount name: {subAccountName}");
        Console.WriteLine($"Created subaccount id: {subAccountId}");
        Console.ResetColor();
        Console.WriteLine();

        await realAveniaApiService.GetAsync($"/v2/account/account-info?subAccountId={subAccountId}");
        return;
    }

    if (accountInfoOnly)
    {
        var subAccountId = Environment.GetEnvironmentVariable("AVENIA_RESUME_SUBACCOUNT_ID");
        var requestUri = string.IsNullOrWhiteSpace(subAccountId)
            ? "/v2/account/account-info"
            : $"/v2/account/account-info?subAccountId={subAccountId}";

        await realAveniaApiService.GetAsync(requestUri);
        return;
    }

    var realOnboardingService = new RealAveniaOnboardingService(realAveniaApiService);

    await realOnboardingService.RunFullOnboardingAsync();
}
else
{
    Console.WriteLine("Mode: MOCK APIs, sample data, no external dependencies.");
    Console.WriteLine("Run with --real and Avenia environment variables to call the sandbox.");
    Console.WriteLine();

    var mockAveniaApiService = new MockAveniaApiService();
    var onboardingService = new OnboardingService(mockAveniaApiService);

    await onboardingService.RunFullOnboardingAsync();
}
