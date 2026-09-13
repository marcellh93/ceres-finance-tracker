using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Admin;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Models;
using ProjectCeres.Specs.Support;
using ProjectCeres.Tests.Integration.Authentication;
using Reqnroll;

namespace ProjectCeres.Specs.Steps;

/// <summary>
/// Drives the real support-ticket endpoints (Stage 12.6/12.5.2) through
/// <see cref="SpecsAuthFactory"/> — the same auth pipeline (UseTestAuthHandler=false)
/// SupportConversationApiTests / SupportAdminApiTests exercise. Marker-isolated: the
/// ticket subject carries a per-scenario GUID so assertions never see another
/// scenario's row on the shared test DB.
/// </summary>
[Binding]
public sealed class SupportTicketSteps
{
    private readonly SpecsAuthFactory _factory;

    private readonly Guid _marker = Guid.NewGuid();
    private HttpClient _userClient = null!;
    private string _userSession = null!;
    private Guid _userId;
    private Guid _ticketId;

    public SupportTicketSteps(SpecsAuthFactory factory) => _factory = factory;

    [Given("a signed-in user")]
    public async Task GivenASignedInUser()
    {
        var user = await AuthTestFixture.RegisterUserAsync(
            _factory, $"support-spec-{_marker:N}@support-spec-test.local");
        _userId = user.Id;
        _userClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        _userSession = await AuthTestFixture.LoginViaHttpAsync(_factory, _userClient, user.Email!);
        _userClient.DefaultRequestHeaders.Add(
            "Cookie", $"{SessionConstants.SessionCookieName}={_userSession}");
    }

    [Given("the user has an open support ticket")]
    public async Task GivenAnOpenTicket()
    {
        var response = await _userClient.SendAsync(PostAsUser("/api/support/tickets", new
        {
            subject = $"spec ticket {_marker:N}",
            message = "Something went wrong.",
            priority = SupportTicketPriority.Normal,
        }));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        _ticketId = created.GetProperty("id").GetGuid();
    }

    [When("the user replies to the ticket")]
    public async Task WhenUserReplies()
    {
        var response = await _userClient.SendAsync(PostAsUser(
            $"/api/support/tickets/{_ticketId}/messages",
            new { body = $"{_marker:N} user follow-up" }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Builds a CSRF-attached POST bound to the signed-in user, mirroring PostMessage() in SupportAdminApiTests.</summary>
    private HttpRequestMessage PostAsUser(string url, object payload)
    {
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, _userId);
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        request.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={_userSession}; {SessionConstants.CsrfCookieName}={csrfCookie}");
        request.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);
        return request;
    }

    [When("an operator replies to the ticket setting the status to \"(.*)\"")]
    public async Task WhenOperatorRepliesSettingTheStatusTo(string status)
    {
        var targetStatus = Enum.Parse<SupportTicketStatus>(status);

        var admin = await AuthTestFixture.RegisterUserAsync(
            _factory, $"support-spec-admin-{_marker:N}@support-spec-test.local");
        var adminClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var adminSession = await AuthTestFixture.LoginViaHttpAsync(_factory, adminClient, admin.Email!);

        using (var scope = _factory.Services.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<AdminRoleService>();
            await roles.GrantAsync(admin.Id);
        }

        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, admin.Id);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/support/tickets/{_ticketId}/messages")
        {
            Content = JsonContent.Create(new
            {
                body = $"{_marker:N} operator reply",
                status = targetStatus,
            }),
        };
        request.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={adminSession}; {SessionConstants.CsrfCookieName}={csrfCookie}");
        request.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);

        var response = await adminClient.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Then("the ticket status is \"(.*)\"")]
    public async Task ThenTheTicketStatusIs(string expected)
    {
        var expectedStatus = Enum.Parse<SupportTicketStatus>(expected);

        var response = await _userClient.GetAsync($"/api/support/tickets/{_ticketId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var thread = await response.Content.ReadFromJsonAsync<JsonElement>();

        thread.GetProperty("status").GetInt32().Should().Be((int)expectedStatus);
    }
}
