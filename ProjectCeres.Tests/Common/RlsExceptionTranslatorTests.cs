using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectCeres.Common;
using ProjectCeres.Common.Exceptions;
using ProjectCeres.Tests.Integration;

namespace ProjectCeres.Tests.Common;

/// <summary>
/// Stage 7.6.2 — unit tests for <see cref="RlsExceptionTranslator"/>. Verifies the
/// translator wraps 42501 on user-owned tables into <see cref="RlsPolicyViolationException"/>
/// and leaves everything else alone. Static helper invoked from
/// <c>AppDbContext.SaveChangesAsync</c>'s catch block; EF interceptor hooks are
/// observational only and can't replace the propagating exception.
/// </summary>
public class RlsExceptionTranslatorTests
{
    [Fact]
    public void Translates_42501_on_a_user_owned_table_to_RlsPolicyViolationException()
    {
        var user = new FakeCurrentUserAccessor(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var pg = MakePostgresException("42501", tableName: "Accounts");
        var wrapped = new DbUpdateException("oh no", pg);

        var translated = RlsExceptionTranslator.TryTranslate(wrapped, user, out var result);

        translated.Should().BeTrue();
        result.Should().NotBeNull();
        result!.TableName.Should().Be("Accounts");
        result.GucUserId.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        result.OriginalException.Should().BeSameAs(pg);
    }

    [Fact]
    public void Leaves_42501_on_a_non_user_owned_table_alone()
    {
        // E.g. "permission denied for table __EFMigrationsHistory" — same SqlState,
        // not an RLS violation.
        var user = new FakeCurrentUserAccessor(Guid.NewGuid());
        var pg = MakePostgresException("42501", tableName: "__EFMigrationsHistory");
        var wrapped = new DbUpdateException("oh no", pg);

        var translated = RlsExceptionTranslator.TryTranslate(wrapped, user, out var result);

        translated.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void Leaves_non_42501_PostgresExceptions_alone()
    {
        // 23505 unique-constraint, 23503 FK, 23502 NOT NULL — all pass through.
        // The DbExceptionTranslator (sub-stage 7.6.5) handles those.
        var user = new FakeCurrentUserAccessor(Guid.NewGuid());
        var pg = MakePostgresException("23505", tableName: "Accounts");
        var wrapped = new DbUpdateException("oh no", pg);

        var translated = RlsExceptionTranslator.TryTranslate(wrapped, user, out var result);

        translated.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void Leaves_DbUpdateException_without_PostgresException_alone()
    {
        var user = new FakeCurrentUserAccessor(Guid.NewGuid());
        var wrapped = new DbUpdateException("not a pg error", new InvalidOperationException("not pg"));

        var translated = RlsExceptionTranslator.TryTranslate(wrapped, user, out var result);

        translated.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void Falls_back_to_message_parsing_when_TableName_field_is_empty()
    {
        // Postgres does NOT populate the structured TableName field for RLS WITH CHECK
        // violations — the table name lives only in the message text. The translator
        // must parse it out.
        var user = new FakeCurrentUserAccessor(Guid.NewGuid());
        var pg = MakePostgresException(
            "42501",
            tableName: null,
            messageText: "new row violates row-level security policy for table \"Accounts\"");
        var wrapped = new DbUpdateException("oh no", pg);

        var translated = RlsExceptionTranslator.TryTranslate(wrapped, user, out var result);

        translated.Should().BeTrue();
        result!.TableName.Should().Be("Accounts");
    }

    [Fact]
    public void GucUserId_is_null_when_accessor_returns_Guid_Empty()
    {
        // BackgroundJobScope leak / pre-auth path / test forgot to bind a user.
        // The diagnostic should report "(unset)" instead of pretending Guid.Empty is a user.
        var user = new FakeCurrentUserAccessor(Guid.Empty);
        var pg = MakePostgresException("42501", tableName: "Accounts");
        var wrapped = new DbUpdateException("oh no", pg);

        var translated = RlsExceptionTranslator.TryTranslate(wrapped, user, out var result);

        translated.Should().BeTrue();
        result!.GucUserId.Should().BeNull();
    }

    // --- helpers ---------------------------------------------------------

    private static PostgresException MakePostgresException(
        string sqlState,
        string? tableName,
        string? messageText = null)
    {
        var pg = new PostgresException(messageText ?? "test", "ERROR", "ERROR", sqlState);
        if (tableName is not null)
        {
            var backing = typeof(PostgresException).GetField("<TableName>k__BackingField",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (backing is not null)
            {
                backing.SetValue(pg, tableName);
            }
            else
            {
                var prop = typeof(PostgresException).GetProperty(nameof(PostgresException.TableName));
                prop?.SetValue(pg, tableName);
            }
        }
        return pg;
    }
}
