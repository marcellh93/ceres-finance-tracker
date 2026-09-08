using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using ProjectCeres.Common;
using ProjectCeres.Models;
using ProjectCeres.Services;
using ProjectCeres.Tests.Common;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// SupportTicketService against the real project_ceres_test database.
///
/// The follow-up rule has two halves. "Same owner" is now ALSO enforced structurally —
/// Stage 12.5 A1 made the self-FK composite (PrecedingTicketId, UserId) → (Id, UserId), so
/// the database rejects a cross-user PrecedingTicketId (pinned by `ParityTests`). The other
/// half — a follow-up must reference a *Closed* ticket, and close is final (no path back out
/// of Closed) — is a state-machine rule the schema cannot express, so it is only ever as
/// strong as these tests.
/// </summary>
[Collection("IntegrationTests")]
public class SupportTicketServiceTests : IAsyncLifetime
{
    private static readonly Guid Sentinel = new("00000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherUser = new("00000000-0000-0000-0000-0000000000ff");

    private readonly TestDbFixture _fixture = new();
    private SupportTicketService _service = null!;
    private FileAttachmentService _attachments = null!;
    private string _tempRoot = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitAsync();

        _tempRoot = Path.Combine(Path.GetTempPath(), $"ceres-support-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(_tempRoot);

        var user = new FakeCurrentUserAccessor(Sentinel);
        _attachments = new FileAttachmentService(_fixture.Db, env.Object, user, TimeProvider.System);
        _service = new SupportTicketService(_fixture.Db, user, TimeProvider.System);
    }

    public async Task DisposeAsync()
    {
        if (_foreignTicketIds.Count > 0)
        {
            // Attachment -> message and message -> ticket are both Cascade, so deleting
            // the tickets takes their messages and attachments with them.
            await using var admin = _fixture.CreateAdminContext();
            await admin.SupportTickets
                .Where(t => _foreignTicketIds.Contains(t.Id)).ExecuteDeleteAsync();
        }

        await _fixture.DisposeAsync();
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static byte[] MinimalJpegBytes() =>
    [
        0xFF, 0xD8, 0xFF, 0xE0,
        0x00, 0x10,
        0x4A, 0x46, 0x49, 0x46, 0x00,
        0x01, 0x01,
        0x00,
        0x00, 0x01, 0x00, 0x01,
        0x00, 0x00,
        0xFF, 0xD9
    ];

    private static IFormFile MakeFormFile(byte[] bytes, string fileName, string contentType) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    /// <summary>
    /// A ticket AND its first message, owned by someone other than the current user.
    ///
    /// Inserted through the admin (BYPASSRLS) context on purpose: the app-role connection
    /// the fixture normally uses is subject to the same RLS policy as production, so it
    /// physically cannot write a row it would not own. That the fixture cannot forge this
    /// row is the wall working — but the tests below need the row to exist in order to
    /// prove the SERVICE also refuses to reach it.
    /// </summary>
    private async Task<(SupportTicket Ticket, SupportMessage Message)> InsertForeignTicketAsync(SupportTicketStatus status)
    {
        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(),
            UserId = OtherUser,
            Subject = $"foreign {Guid.NewGuid():N}",
            Status = status,
            Priority = SupportTicketPriority.Normal,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        var message = new SupportMessage
        {
            Id = Guid.NewGuid(),
            UserId = OtherUser,
            SupportTicketId = ticket.Id,
            AuthorRole = SupportMessageAuthor.User,
            Body = "not yours",
            CreatedAt = DateTime.UtcNow,
        };

        await using var admin = _fixture.CreateAdminContext();
        admin.SupportTickets.Add(ticket);
        admin.SupportMessages.Add(message);
        await admin.SaveChangesAsync();
        _foreignTicketIds.Add(ticket.Id);
        return (ticket, message);
    }

    /// <summary>
    /// Rows written through the admin context sit outside the fixture's transaction, so
    /// the automatic rollback does not reach them. Deleted explicitly on teardown.
    /// </summary>
    private readonly List<Guid> _foreignTicketIds = [];

    /// <summary>The first (only) message CreateAsync wrote for the given ticket subject.</summary>
    private async Task<SupportMessage> GetFirstMessageAsync(string subject) =>
        await _fixture.Db.SupportMessages.AsNoTracking()
            .SingleAsync(m => m.SupportTicket.Subject == subject);

    // -------------------------------------------------------------------------
    // CreateAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_opens_the_ticket_and_stamps_the_current_user()
    {
        var subject = $"Cannot export {Guid.NewGuid():N}";

        var ticket = await _service.CreateAsync(subject, "The CSV comes out empty.", SupportTicketPriority.High);

        ticket.Status.Should().Be(SupportTicketStatus.Open, "a new ticket is always Open");
        ticket.UserId.Should().Be(Sentinel);
        ticket.Priority.Should().Be(SupportTicketPriority.High);
        ticket.CreatedAt.Should().Be(ticket.UpdatedAt, "nothing has updated it yet");
        ticket.PrecedingTicketId.Should().BeNull();

        var persisted = await _fixture.Db.SupportTickets.AsNoTracking()
            .SingleAsync(t => t.Subject == subject);
        persisted.Id.Should().Be(ticket.Id);

        var firstMessage = await GetFirstMessageAsync(subject);
        firstMessage.AuthorRole.Should().Be(SupportMessageAuthor.User, "the ticket text IS the first message");
        firstMessage.UserId.Should().Be(Sentinel);
        firstMessage.Body.Should().Be("The CSV comes out empty.");
    }

    [Theory]
    [InlineData("", "message")]
    [InlineData("   ", "message")]
    [InlineData("subject", "")]
    [InlineData("subject", "   ")]
    public async Task CreateAsync_rejects_a_blank_subject_or_message(string subject, string message)
    {
        var act = () => _service.CreateAsync(subject, message, SupportTicketPriority.Normal);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // -------------------------------------------------------------------------
    // ListOwnAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ListOwnAsync_returns_only_the_callers_tickets_newest_first()
    {
        var (foreign, _) = await InsertForeignTicketAsync(SupportTicketStatus.Open);
        var older = await _service.CreateAsync($"older {Guid.NewGuid():N}", "m", SupportTicketPriority.Low);
        var newer = await _service.CreateAsync($"newer {Guid.NewGuid():N}", "m", SupportTicketPriority.Low);

        var list = await _service.ListOwnAsync();

        list.Should().NotContain(t => t.Id == foreign.Id, "the query filter scopes to the owner");
        var mine = list.Where(t => t.Id == older.Id || t.Id == newer.Id).ToList();
        mine.Should().HaveCount(2);
        list.Select(t => t.Id).ToList().IndexOf(newer.Id)
            .Should().BeLessThan(list.Select(t => t.Id).ToList().IndexOf(older.Id),
                "newest first, so the most recent report is what you see");
    }

    // -------------------------------------------------------------------------
    // CloseAsync — close is final
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CloseAsync_closes_an_open_ticket_and_moves_UpdatedAt()
    {
        var ticket = await _service.CreateAsync($"close me {Guid.NewGuid():N}", "m", SupportTicketPriority.Normal);
        var createdUpdatedAt = ticket.UpdatedAt;

        await Task.Delay(10);
        (await _service.CloseAsync(ticket.Id)).Should().Be(CloseTicketResult.Closed);

        var persisted = await _fixture.Db.SupportTickets.AsNoTracking().SingleAsync(t => t.Id == ticket.Id);
        persisted.Status.Should().Be(SupportTicketStatus.Closed);
        persisted.UpdatedAt.Should().BeAfter(createdUpdatedAt);
    }

    [Fact]
    public async Task CloseAsync_refuses_to_close_an_already_closed_ticket()
    {
        var ticket = await _service.CreateAsync($"twice {Guid.NewGuid():N}", "m", SupportTicketPriority.Normal);
        await _service.CloseAsync(ticket.Id);

        var result = await _service.CloseAsync(ticket.Id);

        result.Should().Be(CloseTicketResult.AlreadyClosed,
            "closing is final — there is no reopen, so a second close is a contradiction");
    }

    [Fact]
    public async Task CloseAsync_cannot_reach_another_users_ticket()
    {
        var (foreign, _) = await InsertForeignTicketAsync(SupportTicketStatus.Open);

        var result = await _service.CloseAsync(foreign.Id);

        result.Should().Be(CloseTicketResult.NotFound,
            "IDOR — someone else's ticket must be indistinguishable from one that does not exist");

        // Reading another user's row needs BOTH escapes: the admin connection to get past
        // Postgres RLS, and IgnoreQueryFilters to get past EF's per-user filter. They are
        // independent walls — dropping either one alone still returns nothing.
        await using var admin = _fixture.CreateAdminContext();
        var untouched = await admin.SupportTickets.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(t => t.Id == foreign.Id);
        untouched.Status.Should().Be(SupportTicketStatus.Open, "and it must actually be unchanged");
    }

    // -------------------------------------------------------------------------
    // Follow-up chain — the rules the FK cannot express
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_links_a_follow_up_to_the_closed_ticket_it_continues()
    {
        var originalSubject = $"original {Guid.NewGuid():N}";
        var original = await _service.CreateAsync(originalSubject, "m", SupportTicketPriority.Normal);
        (await _service.CloseAsync(original.Id)).Should().Be(CloseTicketResult.Closed);

        var followUpSubject = $"follow up {Guid.NewGuid():N}";
        var followUp = await _service.CreateAsync(
            followUpSubject, "still broken", SupportTicketPriority.High, original.Id);

        followUp.PrecedingTicketId.Should().Be(original.Id);
        followUp.Status.Should().Be(SupportTicketStatus.Open, "a follow-up is a new ticket with its own lifecycle");

        var followUpMessage = await GetFirstMessageAsync(followUpSubject);
        followUpMessage.AuthorRole.Should().Be(SupportMessageAuthor.User);
        followUpMessage.UserId.Should().Be(Sentinel);
        followUpMessage.Body.Should().Be("still broken");
    }

    [Fact]
    public async Task CreateAsync_refuses_a_follow_up_to_a_ticket_that_is_still_open()
    {
        var open = await _service.CreateAsync($"still open {Guid.NewGuid():N}", "m", SupportTicketPriority.Normal);

        var act = () => _service.CreateAsync("follow up", "m", SupportTicketPriority.Normal, open.Id);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "a follow-up continues a finished conversation; the open one should just be updated");
    }

    [Fact]
    public async Task CreateAsync_refuses_a_follow_up_to_another_users_ticket()
    {
        var (foreign, _) = await InsertForeignTicketAsync(SupportTicketStatus.Closed);

        var act = () => _service.CreateAsync("follow up", "m", SupportTicketPriority.Normal, foreign.Id);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "the RI trigger bypasses RLS, so the database alone would accept this cross-user reference");
    }

    // -------------------------------------------------------------------------
    // Attachments
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UploadForSupportMessageAsync_writes_the_file_and_stamps_the_owner()
    {
        var subject = $"with file {Guid.NewGuid():N}";
        await _service.CreateAsync(subject, "m", SupportTicketPriority.Normal);
        var message = await GetFirstMessageAsync(subject);
        var file = MakeFormFile(MinimalJpegBytes(), "screenshot.jpg", "image/jpeg");

        var attachment = await _attachments.UploadForSupportMessageAsync(message.Id, file);

        attachment.SupportMessageId.Should().Be(message.Id);
        attachment.UserId.Should().Be(Sentinel,
            "the composite FK is (SupportMessageId, UserId) — an unset UserId cannot satisfy it");
        attachment.ContentType.Should().Be("image/jpeg");
        attachment.FileName.Should().Be("screenshot.jpg", "the original name is kept for display");
        attachment.StoredPath.Should().NotContain("screenshot",
            "the stored path is system-generated; no user string reaches the filesystem");

        File.Exists(Path.Combine(_tempRoot, attachment.StoredPath)).Should().BeTrue();
    }

    [Fact]
    public async Task UploadForSupportMessageAsync_rejects_a_disallowed_type_by_content_not_extension()
    {
        var subject = $"bad type {Guid.NewGuid():N}";
        await _service.CreateAsync(subject, "m", SupportTicketPriority.Normal);
        var message = await GetFirstMessageAsync(subject);
        // Claims to be a JPEG by name and header; the bytes are a Windows executable.
        var file = MakeFormFile([0x4D, 0x5A, 0x90, 0x00, 0x03], "totally-an-image.jpg", "image/jpeg");

        var act = () => _attachments.UploadForSupportMessageAsync(message.Id, file);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Accepted types: JPEG, PNG, GIF, WebP, PDF*");
    }

    [Fact]
    public async Task UploadForSupportMessageAsync_rejects_a_file_over_the_10_MB_cap()
    {
        var subject = $"too big {Guid.NewGuid():N}";
        await _service.CreateAsync(subject, "m", SupportTicketPriority.Normal);
        var message = await GetFirstMessageAsync(subject);
        // 11 MB. The cap lives in the core the three attachment families now share, so
        // this also guards against a refactor that drops it for support only.
        var file = MakeFormFile(new byte[11 * 1024 * 1024], "huge.jpg", "image/jpeg");

        var act = () => _attachments.UploadForSupportMessageAsync(message.Id, file);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*10 MB*");
    }

    [Fact]
    public async Task UploadForSupportMessageAsync_caps_a_message_at_ten_attachments()
    {
        var subject = $"many files {Guid.NewGuid():N}";
        await _service.CreateAsync(subject, "m", SupportTicketPriority.Normal);
        var message = await GetFirstMessageAsync(subject);
        for (var i = 0; i < 10; i++)
        {
            await _attachments.UploadForSupportMessageAsync(
                message.Id, MakeFormFile(MinimalJpegBytes(), $"shot{i}.jpg", "image/jpeg"));
        }

        var act = () => _attachments.UploadForSupportMessageAsync(
            message.Id, MakeFormFile(MinimalJpegBytes(), "eleventh.jpg", "image/jpeg"));

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*more than 10*");
    }

    [Fact]
    public async Task UploadForSupportMessageAsync_refuses_a_write_past_the_per_user_storage_quota()
    {
        // security-model.md § File Access Control requires a per-user total, not just a
        // per-parent cap: 10 files x 10 MB bounds one message, but nothing bounds how many
        // tickets/messages a user opens. Seed the quota as already-consumed rather than
        // uploading 500 MB, which would make this test unusable.
        var subject = $"quota {Guid.NewGuid():N}";
        await _service.CreateAsync(subject, "m", SupportTicketPriority.Normal);
        var message = await GetFirstMessageAsync(subject);
        _fixture.Db.SupportTicketAttachments.Add(new SupportTicketAttachment
        {
            Id = Guid.NewGuid(),
            SupportMessageId = message.Id,
            UserId = Sentinel,
            FileName = "already-used.pdf",
            StoredPath = Path.Combine("uploads", "support", message.Id.ToString(), "prior.pdf"),
            ContentType = "application/pdf",
            FileSizeBytes = 500L * 1024 * 1024,
            UploadedAt = DateTime.UtcNow,
        });
        await _fixture.Db.SaveChangesAsync();

        var act = () => _attachments.UploadForSupportMessageAsync(
            message.Id, MakeFormFile(MinimalJpegBytes(), "one-more.jpg", "image/jpeg"));

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Storage limit reached*");
    }

    [Fact]
    public async Task UploadForSupportMessageAsync_cannot_attach_to_another_users_message()
    {
        var (_, foreignMessage) = await InsertForeignTicketAsync(SupportTicketStatus.Open);
        var file = MakeFormFile(MinimalJpegBytes(), "shot.jpg", "image/jpeg");

        var act = () => _attachments.UploadForSupportMessageAsync(foreignMessage.Id, file);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "this is the cross-tenant write the composite FK was added to close");
    }

    [Fact]
    public async Task GetSupportTicketAttachmentAsync_cannot_read_another_users_attachment()
    {
        var (foreign, foreignMessage) = await InsertForeignTicketAsync(SupportTicketStatus.Open);
        var row = new SupportTicketAttachment
        {
            Id = Guid.NewGuid(),
            SupportMessageId = foreignMessage.Id,
            UserId = OtherUser,
            FileName = "theirs.jpg",
            StoredPath = Path.Combine("uploads", "support", foreign.Id.ToString(), "x.jpg"),
            ContentType = "image/jpeg",
            FileSizeBytes = 10,
            UploadedAt = DateTime.UtcNow,
        };
        await using (var admin = _fixture.CreateAdminContext())
        {
            admin.SupportTicketAttachments.Add(row);
            await admin.SaveChangesAsync();
        }

        var act = () => _attachments.GetSupportTicketAttachmentAsync(row.Id);

        await act.Should().ThrowAsync<InvalidOperationException>("IDOR on the download path");
    }
}
