using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProjectCeres.Data;
using ProjectCeres.Models;

namespace ProjectCeres.Tests.Integration.Api;

/// <summary>
/// The support-ticket HTTP surface.
///
/// The IDOR cases matter most here: a ticket or attachment belonging to someone else must
/// answer 404, never 403, so the response cannot be used to probe which ids exist.
/// </summary>
[Collection("IntegrationTests")]
public class SupportApiTests : IAsyncLifetime
{
    private static readonly Guid Sentinel = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherUser = new("00000000-0000-0000-0000-0000000000fe");

    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<Guid> _seededTicketIds = [];
    private readonly List<Guid> _foreignTicketIds = [];

    public SupportApiTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var all = _seededTicketIds.Concat(_foreignTicketIds).ToList();
        if (all.Count > 0)
        {
            await db.SupportTicketAttachments.IgnoreQueryFilters()
                .Where(a => all.Contains(a.SupportTicketId)).ExecuteDeleteAsync();
            await db.SupportTickets.IgnoreQueryFilters()
                .Where(t => all.Contains(t.Id)).ExecuteDeleteAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static byte[] MinimalJpegBytes() =>
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10,
        0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00,
        0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9
    ];

    private async Task<Guid> CreateTicketAsync(string? subject = null, SupportTicketPriority priority = SupportTicketPriority.Normal)
    {
        var response = await _client.PostAsJsonAsync("/api/support/tickets", new
        {
            subject = subject ?? $"Subject {Guid.NewGuid():N}",
            message = "Something went wrong.",
            priority,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();
        _seededTicketIds.Add(id);
        return id;
    }

    /// <summary>A ticket owned by someone else, written past RLS via the seeding context.</summary>
    private async Task<Guid> SeedForeignTicketAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(),
            UserId = OtherUser,
            Subject = $"Theirs {Guid.NewGuid():N}",
            Message = "not yours",
            Status = SupportTicketStatus.Open,
            Priority = SupportTicketPriority.Normal,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.SupportTickets.Add(ticket);
        await db.SaveChangesAsync();
        _foreignTicketIds.Add(ticket.Id);
        return ticket.Id;
    }

    private static MultipartFormDataContent FileContent(byte[] bytes, string name, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { content, "file", name } };
    }

    // -------------------------------------------------------------------------
    // Create
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Create_returns_201_with_a_location_header_and_opens_the_ticket()
    {
        var subject = $"Export is broken {Guid.NewGuid():N}";

        var response = await _client.PostAsJsonAsync("/api/support/tickets", new
        {
            subject,
            message = "The CSV comes out empty.",
            priority = SupportTicketPriority.High,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull("201 carries a Location per the api contract");

        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _seededTicketIds.Add(id);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await db.SupportTickets.AsNoTracking().SingleAsync(t => t.Id == id);
        persisted.Status.Should().Be(SupportTicketStatus.Open);
        persisted.UserId.Should().Be(Sentinel);
    }

    [Fact]
    public async Task Create_writes_a_SupportTicketCreated_audit_row_naming_the_ticket()
    {
        var id = await CreateTicketAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Keyed on the ticket id, not a time window: the shared test database is
        // sequential and full of other runs' rows, so a window query would pass on
        // somebody else's audit row and keep passing if this feature were removed.
        var written = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(a => a.EntityId == id);

        written.Action.Should().Be(AuditLogAction.SupportTicketCreated);
        written.UserId.Should().Be(Sentinel);
        written.EntityType.Should().Be(nameof(SupportTicket),
            "the row must say WHICH kind of thing it refers to, or EntityId is ambiguous");
    }

    [Theory]
    [InlineData("", "a message")]
    [InlineData("a subject", "")]
    public async Task Create_rejects_a_missing_subject_or_message_with_422(string subject, string message)
    {
        var response = await _client.PostAsJsonAsync("/api/support/tickets", new
        {
            subject,
            message,
            priority = SupportTicketPriority.Normal,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // -------------------------------------------------------------------------
    // List / get — own only
    // -------------------------------------------------------------------------

    [Fact]
    public async Task List_returns_only_the_callers_own_tickets()
    {
        var mine = await CreateTicketAsync();
        var theirs = await SeedForeignTicketAsync();

        var response = await _client.GetAsync("/api/support/tickets");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var ids = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).ToList();

        ids.Should().Contain(mine);
        ids.Should().NotContain(theirs, "the query filter scopes the list to its owner");
    }

    [Fact]
    public async Task Get_another_users_ticket_is_404_not_403()
    {
        var theirs = await SeedForeignTicketAsync();

        var response = await _client.GetAsync($"/api/support/tickets/{theirs}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "403 would confirm the id exists; 404 leaks nothing");
    }

    // -------------------------------------------------------------------------
    // Close
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Close_closes_the_ticket_and_a_second_close_is_422()
    {
        var id = await CreateTicketAsync();

        var first = await _client.PostAsync($"/api/support/tickets/{id}/close", null);
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await _client.PostAsync($"/api/support/tickets/{id}/close", null);
        second.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "closing is final — there is no reopen, so a second close is a contradiction");
        (await second.Content.ReadAsStringAsync()).Should().Contain("TICKET_ALREADY_CLOSED",
            "a machine-readable code, so the SPA need not string-match server English");
    }

    [Fact]
    public async Task Close_another_users_ticket_is_404_and_leaves_it_open()
    {
        var theirs = await SeedForeignTicketAsync();

        var response = await _client.PostAsync($"/api/support/tickets/{theirs}/close", null);

        // 404, matching GET on the same id. A 422 here would have been a different shape
        // from the sibling endpoint for the identical condition, and echoed the id back.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "someone else's ticket must be indistinguishable from one that does not exist");
        (await response.Content.ReadAsStringAsync()).Should().NotContain(theirs.ToString(),
            "a not-found body must not echo the id that was probed");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var untouched = await db.SupportTickets.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(t => t.Id == theirs);
        untouched.Status.Should().Be(SupportTicketStatus.Open);
    }

    [Fact]
    public async Task Close_an_unknown_ticket_is_also_404()
    {
        var response = await _client.PostAsync($"/api/support/tickets/{Guid.NewGuid()}/close", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "and it must be the SAME answer as a foreign ticket, or the pair is an oracle");
    }

    // -------------------------------------------------------------------------
    // Follow-up chain — the wire format, which the service tests cannot reach
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Create_links_a_follow_up_to_the_closed_ticket_it_continues()
    {
        var original = await CreateTicketAsync();
        (await _client.PostAsync($"/api/support/tickets/{original}/close", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await _client.PostAsJsonAsync("/api/support/tickets", new
        {
            subject = $"Follow-up {Guid.NewGuid():N}",
            message = "Still broken after the last ticket was closed.",
            priority = SupportTicketPriority.High,
            precedingTicketId = original,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var followUpId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        _seededTicketIds.Add(followUpId);

        // The persisted link is the assertion that matters. A 201 alone would also be
        // returned if precedingTicketId silently failed to bind — the follow-up would
        // just be a standalone ticket, which looks identical from the status code.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await db.SupportTickets.AsNoTracking().SingleAsync(t => t.Id == followUpId);
        persisted.PrecedingTicketId.Should().Be(original,
            "the follow-up must carry context from the ticket it continues");
        persisted.Status.Should().Be(SupportTicketStatus.Open, "a follow-up has its own lifecycle");
    }

    [Fact]
    public async Task Create_of_a_follow_up_to_another_users_ticket_is_422()
    {
        var theirs = await SeedForeignTicketAsync();

        var response = await _client.PostAsJsonAsync("/api/support/tickets", new
        {
            subject = $"Piggyback {Guid.NewGuid():N}",
            message = "Trying to chain onto someone else's ticket.",
            priority = SupportTicketPriority.Normal,
            precedingTicketId = theirs,
        });

        // The RI trigger bypasses RLS, so the database alone would accept this reference.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Create_of_a_follow_up_to_a_still_open_ticket_is_422()
    {
        var open = await CreateTicketAsync();

        var response = await _client.PostAsJsonAsync("/api/support/tickets", new
        {
            subject = $"Premature follow-up {Guid.NewGuid():N}",
            message = "The original is still open.",
            priority = SupportTicketPriority.Normal,
            precedingTicketId = open,
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // -------------------------------------------------------------------------
    // Attachments
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Upload_then_download_round_trips_the_file()
    {
        var id = await CreateTicketAsync();

        var upload = await _client.PostAsync($"/api/support/tickets/{id}/attachments",
            FileContent(MinimalJpegBytes(), "screenshot.jpg", "image/jpeg"));
        upload.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await upload.Content.ReadFromJsonAsync<JsonElement>();
        var attachmentId = dto.GetProperty("id").GetGuid();
        dto.GetProperty("fileName").GetString().Should().Be("screenshot.jpg");

        var download = await _client.GetAsync($"/api/attachments/support/{attachmentId}");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment",
            "never inline — an inline HTML-ish payload would render in the browser origin");
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(MinimalJpegBytes());
    }

    [Fact]
    public async Task Upload_of_a_disallowed_type_is_422_with_the_friendly_message()
    {
        var id = await CreateTicketAsync();

        // Names itself a JPEG; the bytes are a Windows executable.
        var response = await _client.PostAsync($"/api/support/tickets/{id}/attachments",
            FileContent([0x4D, 0x5A, 0x90, 0x00, 0x03], "totally-an-image.jpg", "image/jpeg"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync())
            .Should().Contain("Accepted types: JPEG, PNG, GIF, WebP, PDF");
    }

    [Fact]
    public async Task Upload_to_another_users_ticket_is_422()
    {
        var theirs = await SeedForeignTicketAsync();

        var response = await _client.PostAsync($"/api/support/tickets/{theirs}/attachments",
            FileContent(MinimalJpegBytes(), "shot.jpg", "image/jpeg"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "this is the cross-tenant write the composite FK closes at the database too");
    }

    [Fact]
    public async Task Download_of_another_users_attachment_is_404()
    {
        var theirs = await SeedForeignTicketAsync();

        Guid foreignAttachmentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = new SupportTicketAttachment
            {
                Id = Guid.NewGuid(),
                SupportTicketId = theirs,
                UserId = OtherUser,
                FileName = "theirs.jpg",
                StoredPath = Path.Combine("uploads", "support", theirs.ToString(), "x.jpg"),
                ContentType = "image/jpeg",
                FileSizeBytes = 22,
                UploadedAt = DateTime.UtcNow,
            };
            db.SupportTicketAttachments.Add(row);
            await db.SaveChangesAsync();
            foreignAttachmentId = row.Id;
        }

        var response = await _client.GetAsync($"/api/attachments/support/{foreignAttachmentId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_ticket_with_no_attachment_is_perfectly_valid()
    {
        var id = await CreateTicketAsync();

        var response = await _client.GetAsync($"/api/support/tickets/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("attachments").GetArrayLength().Should().Be(0,
                "attachments are optional — most tickets will not have one");
    }
}
