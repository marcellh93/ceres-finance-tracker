using System.Net.Http;
using FluentAssertions;
using ProjectCeres.Tests.Common;
using Xunit;

namespace ProjectCeres.Tests.Integration.Security;

[Collection("IntegrationParallel1")]
public class SecurityHeadersTests : IntegrationTestBase<Bucket1Factory>
{
    private readonly HttpClient _client;
    public SecurityHeadersTests(Bucket1Factory f, Bucket1Database db) : base(f, db)
        => _client = f.CreateClient();

    [Fact]
    public async Task Response_carries_the_static_hardening_headers()
    {
        var res = await _client.GetAsync("/api/health");
        res.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        res.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
        res.Headers.GetValues("Referrer-Policy").Should().Contain("strict-origin-when-cross-origin");
        res.Headers.Contains("Cross-Origin-Opener-Policy").Should().BeTrue();
    }

    [Fact]
    public async Task Csp_is_present_and_script_src_is_not_unsafe_inline()
    {
        var res = await _client.GetAsync("/api/health");
        var csp = string.Join(" ", res.Headers.GetValues("Content-Security-Policy"));
        csp.Should().Contain("default-src 'self'");
        // script-src is strict hash-based; style-src legitimately carries 'unsafe-inline' for
        // recharts' injected inline <style> tags, so the no-'unsafe-inline' guarantee is scoped
        // to script-src alone, not the whole CSP header.
        var scriptSrc = csp.Split(';').Single(d => d.Trim().StartsWith("script-src", StringComparison.Ordinal));
        scriptSrc.Should().NotContain("'unsafe-inline'");
        csp.Should().Contain("object-src 'none'");
    }

    [Fact]
    public async Task Api_response_is_not_cacheable()
    {
        var res = await _client.GetAsync("/api/health");
        var cc = string.Join(" ", res.Headers.TryGetValues("Cache-Control", out var v) ? v : new[] { "" });
        cc.Should().Contain("no-store");
    }

    [Fact]
    public async Task Csp_script_hashes_match_the_served_shell()
    {
        var shell = await _client.GetStringAsync("/");
        var res = await _client.GetAsync("/api/health");
        var csp = string.Join(" ", res.Headers.GetValues("Content-Security-Policy"));
        // Every inline <script> in the served shell must have its hash present in script-src.
        var re = new System.Text.RegularExpressions.Regex(@"<script(?![^>]*\bsrc=)[^>]*>([\s\S]*?)</script>");
        foreach (System.Text.RegularExpressions.Match m in re.Matches(shell))
        {
            var body = m.Groups[1].Value;
            var hash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(body)));
            csp.Should().Contain(hash, "every inline shell script must be blessed by its CSP hash");
        }
    }
}
