using System.Runtime.CompilerServices;
using FluentAssertions;

namespace ProjectCeres.Tests.Integration.Authentication;

/// <summary>
/// Architecture-level pins for the Stage 6.15 O(1) token-verify refactor. Source-text
/// asserts that the three formerly-candidate-loop methods do not load every unconsumed
/// token row into memory anymore — if a future refactor reintroduces a
/// <c>.ToListAsync()</c> on these DbSets the test fails immediately, even if the
/// behaviour tests stay green under a low row count.
///
/// Reads source by resolving the test file path via <see cref="CallerFilePathAttribute"/>
/// at compile-time. Robust to working-directory changes during test runs.
/// </summary>
public class TokenLookupArchitectureTests
{
    [Fact]
    public void PasswordResetService_ConfirmAsync_does_not_iterate_all_tokens()
    {
        var body = ExtractMethodBody(
            servicePath: "Common/Authentication/PasswordResetService.cs",
            methodHeaderPattern: "public async Task<PasswordResetConfirmOutcome> ConfirmAsync");

        body.Should().NotContain("ToListAsync",
            "ConfirmAsync must locate the token via the indexed TokenLookup column — ToListAsync on the PasswordResetTokens DbSet is the regression pattern Stage 6.15 closed");
    }

    [Fact]
    public void EmailChangeService_ConfirmAsync_does_not_iterate_all_tokens()
    {
        var body = ExtractMethodBody(
            servicePath: "Common/Authentication/EmailChangeService.cs",
            methodHeaderPattern: "public async Task<EmailChangeConfirmOutcome> ConfirmAsync");

        body.Should().NotContain("ToListAsync",
            "ConfirmAsync must locate the token via the indexed TokenLookup column — ToListAsync on the EmailChangeTokens DbSet is the regression pattern Stage 6.15 closed");
    }

    [Fact]
    public void EmailChangeService_RevokeAsync_does_not_iterate_all_tokens()
    {
        var body = ExtractMethodBody(
            servicePath: "Common/Authentication/EmailChangeService.cs",
            methodHeaderPattern: "public async Task<EmailChangeRevokeOutcome> RevokeAsync");

        body.Should().NotContain("ToListAsync",
            "RevokeAsync must locate the token via the indexed TokenLookup column — ToListAsync on the EmailChangeTokens DbSet is the regression pattern Stage 6.15 closed");
    }

    private static string ExtractMethodBody(
        string servicePath,
        string methodHeaderPattern,
        [CallerFilePath] string callerPath = "")
    {
        // Tests live at .../ProjectCeres.Tests/Integration/Authentication/<this>.cs
        // Production sources live at .../ProjectCeres/<servicePath>
        var testsDir = Path.GetDirectoryName(callerPath)!;
        var repoRoot = Path.GetFullPath(Path.Combine(testsDir, "..", "..", ".."));
        var absolute = Path.Combine(repoRoot, "ProjectCeres", servicePath);

        File.Exists(absolute).Should().BeTrue(
            $"production source for the assertion is expected at {absolute}");

        var source = File.ReadAllText(absolute);
        var headerIdx = source.IndexOf(methodHeaderPattern, StringComparison.Ordinal);
        headerIdx.Should().BeGreaterThan(-1,
            $"expected to find method header '{methodHeaderPattern}' in {servicePath}");

        // Walk forward from the header to the opening brace of the method body, then
        // brace-match until the matching closing brace. C# allows nested braces, so
        // depth-count.
        var openIdx = source.IndexOf('{', headerIdx);
        openIdx.Should().BeGreaterThan(-1);

        var depth = 0;
        var closeIdx = -1;
        for (var i = openIdx; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) { closeIdx = i; break; }
            }
        }
        closeIdx.Should().BeGreaterThan(openIdx);

        return source.Substring(openIdx, closeIdx - openIdx + 1);
    }
}
