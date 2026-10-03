using System.Text.Json;

namespace ProjectCeres.Common.Security;

/// <summary>Builds the Ceres security-header policy. CSP is hash-based (static SPA).</summary>
public static class SecurityHeadersConfig
{
    public static HeaderPolicyCollection Build(IWebHostEnvironment env, string distDir)
    {
        var scriptHashes = LoadScriptHashes(distDir);

        var policy = new HeaderPolicyCollection()
            .AddContentTypeOptionsNoSniff()
            .AddFrameOptionsDeny()
            .AddReferrerPolicyStrictOriginWhenCrossOrigin()
            .AddCrossOriginOpenerPolicy(b => b.SameOrigin())
            .AddCrossOriginResourcePolicy(b => b.SameOrigin())
            .AddPermissionsPolicy(b =>
            {
                b.AddCamera().None();
                b.AddMicrophone().None();
                b.AddGeolocation().None();
            });

        policy.AddContentSecurityPolicy(csp =>
        {
            csp.AddDefaultSrc().Self();
            var script = csp.AddScriptSrc().Self();
            foreach (var h in scriptHashes) script.WithHash256(StripPrefix(h));
            csp.AddStyleSrc().Self().UnsafeInline(); // see spec: recharts injects inline <style>; styles only
            csp.AddImgSrc().Self().Data();
            csp.AddFontSrc().Self();
            csp.AddConnectSrc().Self();
            csp.AddObjectSrc().None();
            csp.AddBaseUri().Self();
            csp.AddFrameAncestors().None();
            csp.AddReportUri().To("/api/csp-report");
        });

        return policy;
    }

    private static string StripPrefix(string h) => h.StartsWith("sha256-", StringComparison.Ordinal) ? h["sha256-".Length..] : h;

    private static IReadOnlyList<string> LoadScriptHashes(string distDir)
    {
        var path = Path.Combine(distDir, "csp-hashes.json");
        if (!File.Exists(path)) return Array.Empty<string>();
        try
        {
            return JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? Array.Empty<string>();
        }
        catch (JsonException) { return Array.Empty<string>(); }
        catch (IOException) { return Array.Empty<string>(); }
    }
}
