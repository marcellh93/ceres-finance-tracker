namespace ProjectCeres.Common.RateLimiting;

/// <summary>
/// Config-bound rate-limit permit counts. Defaults equal the pre-9.11 hardcoded
/// literals; only appsettings.E2E.json raises them so the E2E suite (one loopback
/// partition) doesn't 429. Windows stay hardcoded — only permit counts vary.
/// </summary>
public sealed class RateLimitOptions
{
    public int LoginByIpPermitLimit { get; set; } = 10;
    public int CsrfByIpPermitLimit { get; set; } = 60;
    public int EmailByIpPermitLimit { get; set; } = 10;
}
