using System.Net;
using System.Net.Http;
using FluentAssertions;

namespace ProjectCeres.Tests.Integration.Infrastructure;

/// <summary>
/// Stage 7.6.1 — pins the contract of <see cref="HttpAssertions"/>: on a status-code
/// mismatch the assertion-failure message contains the response body. Without this
/// contract, the helper's whole point would be invisible.
/// </summary>
public class HttpAssertionsTests
{
    [Fact]
    public async Task HaveStatusCodeAsync_passes_silently_when_status_matches()
    {
        var resp = new HttpResponseMessage(HttpStatusCode.NoContent);

        Func<Task> act = () => resp.HaveStatusCodeAsync(HttpStatusCode.NoContent);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task HaveStatusCodeAsync_failure_message_includes_response_body()
    {
        var stack = "System.InvalidOperationException: The widget was frobnicated.\n   at Foo.Bar(...)";
        var resp = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(stack),
        };

        Func<Task> act = () => resp.HaveStatusCodeAsync(HttpStatusCode.NoContent);

        var ex = await act.Should().ThrowAsync<Xunit.Sdk.XunitException>();
        ex.Which.Message.Should().Contain("The widget was frobnicated");
        ex.Which.Message.Should().Contain("Foo.Bar");
        ex.Which.Message.Should().Contain("InternalServerError");
    }

    [Fact]
    public async Task HaveStatusCodeAsync_truncates_very_long_bodies()
    {
        var huge = new string('x', 12000);
        var resp = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(huge),
        };

        Func<Task> act = () => resp.HaveStatusCodeAsync(HttpStatusCode.NoContent);

        var ex = await act.Should().ThrowAsync<Xunit.Sdk.XunitException>();
        ex.Which.Message.Should().Contain("truncated");
        // Excerpt is the first 8000 chars; the message overall stays manageable.
        ex.Which.Message.Length.Should().BeLessThan(12000);
    }

    [Fact]
    public async Task BeSuccessfulAsync_passes_silently_on_2xx()
    {
        var resp = new HttpResponseMessage(HttpStatusCode.OK);

        Func<Task> act = () => resp.BeSuccessfulAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task BeSuccessfulAsync_failure_message_includes_response_body()
    {
        var stack = "Npgsql.PostgresException: 42501 permission denied";
        var resp = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(stack),
        };

        Func<Task> act = () => resp.BeSuccessfulAsync();

        var ex = await act.Should().ThrowAsync<Xunit.Sdk.XunitException>();
        ex.Which.Message.Should().Contain("42501");
        ex.Which.Message.Should().Contain("InternalServerError");
    }
}
