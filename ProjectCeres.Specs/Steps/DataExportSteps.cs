using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectCeres.Common.Authentication;
using ProjectCeres.Common.Email;
using ProjectCeres.Data;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.Specs.Support;
using ProjectCeres.Tests.Integration;
using ProjectCeres.Tests.Integration.Authentication;
using ProjectCeres.Tools;
using Reqnroll;

namespace ProjectCeres.Specs.Steps;

/// <summary>
/// Golden path for Stage 13.8: request export -> worker builds+emails -> download once
/// -> second download refused. Drives the real HTTP endpoints (ProfileApiController)
/// through SpecsAuthFactory, and invokes ExportJobWorker.ProcessPendingAsync directly
/// (same call shape as ExportJobWorkerTests) with a CapturingEmailService swapped in so
/// the step can read the emailed download link. Marker-isolated per scenario.
/// </summary>
[Binding]
public sealed class DataExportSteps
{
    private readonly SpecsAuthFactory _factory;
    private readonly List<EmailMessage> _capturedEmails = new();

    private readonly Guid _marker = Guid.NewGuid();
    private HttpClient _userClient = null!;
    private string _userSession = null!;
    private Guid _userId;
    private HttpStatusCode _requestStatus;
    private string? _downloadToken;
    private HttpStatusCode _downloadStatus;
    private byte[]? _downloadBytes;
    private string? _downloadMediaType;

    public DataExportSteps(SpecsAuthFactory factory) => _factory = factory;

    [Given("a signed-in user with recent re-authentication")]
    public async Task GivenASignedInUserWithRecentReauth()
    {
        var user = await AuthTestFixture.RegisterUserAsync(
            _factory, $"data-export-spec-{_marker:N}@data-export-spec-test.local");
        _userId = user.Id;
        _userClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        // LoginViaHttpAsync stamps a fresh LastReauthAt via the real login flow, which
        // satisfies [RequireRecentAuth] on POST /api/profile/export.
        _userSession = await AuthTestFixture.LoginViaHttpAsync(_factory, _userClient, user.Email!);
    }

    [When("they request a data export")]
    public async Task WhenTheyRequestADataExport()
    {
        var (csrfCookie, csrfHeader) = AuthTestFixture.MintCsrf(_factory, _userId);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/profile/export");
        request.Headers.Add("Cookie",
            $"{SessionConstants.SessionCookieName}={_userSession}; {SessionConstants.CsrfCookieName}={csrfCookie}");
        request.Headers.Add(SessionConstants.CsrfHeaderName, csrfHeader);

        var response = await _userClient.SendAsync(request);
        _requestStatus = response.StatusCode;
    }

    [Then("the request is accepted with status 202")]
    public void ThenTheRequestIsAccepted()
    {
        _requestStatus.Should().Be(HttpStatusCode.Accepted);
    }

    [When("the export worker runs")]
    public async Task WhenTheExportWorkerRuns()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<AdminDbContext>();
        var builder = sp.GetRequiredService<DataExportBuilder>();
        var tokens = sp.GetRequiredService<ExportTokenGenerator>();
        var lookupHasher = sp.GetRequiredService<TokenLookupHasher>();
        var composer = sp.GetRequiredService<IEmailComposer>();
        var recipients = sp.GetRequiredService<IEmailRecipientResolver>();
        var languages = sp.GetRequiredService<ILanguageResolver>();
        var emailOptions = sp.GetRequiredService<IOptions<EmailOptions>>();
        var attachmentOptions = sp.GetRequiredService<IOptions<FileAttachmentOptions>>();
        var env = sp.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
        var clock = sp.GetRequiredService<TimeProvider>();

        await ExportJobWorker.ProcessPendingAsync(
            db, builder, tokens, lookupHasher, composer, new CapturingEmailService(_capturedEmails),
            recipients, languages, emailOptions, attachmentOptions, env, clock,
            NullLogger.Instance, CancellationToken.None);
    }

    [Then("they receive an export-ready email with a download link")]
    public void ThenTheyReceiveAnExportReadyEmail()
    {
        _capturedEmails.Should().HaveCount(1, "the worker must send exactly one email for this job");

        using var scope = _factory.Services.CreateScope();
        var localizer = scope.ServiceProvider.GetRequiredService<IStringLocalizer<EmailsResource>>();
        var expectedSubject = localizer["GdprExportReady.Subject", CultureInfo.CurrentUICulture];

        var message = _capturedEmails[0];
        message.Subject.Should().Be(expectedSubject, "the captured email must be the GdprExportReady template");

        _downloadToken = ExtractToken(message.BodyText);
        _downloadToken.Should().NotBeNullOrEmpty("the email body must carry the raw download token");
    }

    [When("they follow the link while signed in")]
    public async Task WhenTheyFollowTheLinkWhileSignedIn()
    {
        await DownloadAsync();
    }

    [Then("the ZIP downloads successfully")]
    public void ThenTheZipDownloadsSuccessfully()
    {
        _downloadStatus.Should().Be(HttpStatusCode.OK);
        _downloadMediaType.Should().Be("application/zip");
        _downloadBytes.Should().NotBeNullOrEmpty();
    }

    [When("they follow the same link again")]
    public async Task WhenTheyFollowTheSameLinkAgain()
    {
        await DownloadAsync();
    }

    [Then("the download is refused as already used")]
    public void ThenTheDownloadIsRefused()
    {
        _downloadStatus.Should().Be((HttpStatusCode)410, "a single-use download link must be gone on the second attempt");
    }

    private async Task DownloadAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/profile/export/download?token={Uri.EscapeDataString(_downloadToken!)}");
        request.Headers.Add("Cookie", $"{SessionConstants.SessionCookieName}={_userSession}");

        var response = await _userClient.SendAsync(request);
        _downloadStatus = response.StatusCode;
        _downloadMediaType = response.Content.Headers.ContentType?.MediaType;
        _downloadBytes = response.StatusCode == HttpStatusCode.OK
            ? await response.Content.ReadAsByteArrayAsync()
            : null;
    }

    private static string? ExtractToken(string bodyText)
    {
        const string marker = "token=";
        var idx = bodyText.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;
        var start = idx + marker.Length;
        var end = start;
        while (end < bodyText.Length && !char.IsWhiteSpace(bodyText[end])) end++;
        return Uri.UnescapeDataString(bodyText[start..end]);
    }
}
