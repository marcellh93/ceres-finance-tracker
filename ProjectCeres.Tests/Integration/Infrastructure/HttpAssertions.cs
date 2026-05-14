using System.Net;
using System.Net.Http;
using FluentAssertions;

namespace ProjectCeres.Tests.Integration.Infrastructure;

/// <summary>
/// Stage 7.6 / sub-stage 7.6.1 — assertion helpers that include the response BODY
/// in the failure message when a status-code assertion fails. Without this, every
/// integration test that hits a 500 prints "Expected NoContent but found
/// InternalServerError" with no clue what threw — three Stage 7.5 commits each
/// burned ~30 minutes writing one-off diagnostic tests to surface the stack trace
/// that was already in the response body.
///
/// <para>
/// In Development (which the WAF inherits), ASP.NET's <c>DeveloperExceptionPage</c>
/// middleware auto-renders unhandled exceptions as the response body. The body is
/// always there; this helper just makes the assertion failure message show it.
/// </para>
///
/// Usage:
/// <code>
///   var resp = await client.SendAsync(req);
///   await resp.Should().HaveStatusCodeAsync(HttpStatusCode.NoContent);
/// </code>
/// </summary>
public static class HttpAssertions
{
    /// <summary>
    /// Asserts the response has the given status code. On failure, reads the
    /// response body and includes it in the assertion message. Awaitable so the
    /// body read can be async; FluentAssertions's normal <c>Should().Be(...)</c>
    /// is synchronous and can't read the body inside the matcher.
    /// </summary>
    public static async Task HaveStatusCodeAsync(
        this HttpResponseMessage response,
        HttpStatusCode expected,
        string because = "")
    {
        if (response.StatusCode == expected)
            return;

        var body = await response.Content.ReadAsStringAsync();
        var bodyExcerpt = body.Length > 8000 ? body[..8000] + "\n…(truncated)" : body;

        var prefix = string.IsNullOrEmpty(because)
            ? string.Empty
            : $" because {because}";

        response.StatusCode.Should().Be(expected,
            $"expected {expected} but got {(int)response.StatusCode} {response.StatusCode}{prefix}.\n" +
            $"Response body:\n{bodyExcerpt}");
    }

    /// <summary>
    /// Asserts the response has a 2xx success status code, including the body
    /// in the failure message otherwise.
    /// </summary>
    public static async Task BeSuccessfulAsync(
        this HttpResponseMessage response,
        string because = "")
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync();
        var bodyExcerpt = body.Length > 8000 ? body[..8000] + "\n…(truncated)" : body;

        var prefix = string.IsNullOrEmpty(because)
            ? string.Empty
            : $" because {because}";

        response.IsSuccessStatusCode.Should().BeTrue(
            $"expected a 2xx success status but got {(int)response.StatusCode} {response.StatusCode}{prefix}.\n" +
            $"Response body:\n{bodyExcerpt}");
    }

    /// <summary>
    /// Reads the response body as a string. Convenience for tests that want to
    /// dump the body on a deliberate failure (e.g. asserting an error code).
    /// </summary>
    public static Task<string> ReadBodyAsync(this HttpResponseMessage response) =>
        response.Content.ReadAsStringAsync();
}
