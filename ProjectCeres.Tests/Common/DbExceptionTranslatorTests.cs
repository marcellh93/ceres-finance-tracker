using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProjectCeres.Common;
using ProjectCeres.Common.Exceptions;

namespace ProjectCeres.Tests.Common;

/// <summary>
/// Stage 7.6.5 — unit tests for <see cref="DbExceptionTranslator"/>. Translates the
/// three Postgres SqlState codes that surface in user-facing flows (23505 unique,
/// 23503 foreign-key, 23502 not-null) into typed exceptions; leaves 42501 (handled
/// by <see cref="RlsExceptionTranslator"/>) and unmatched codes alone.
///
/// <para>
/// Empirical probe of PG 18.3 against <c>project_ceres_test</c> (this stage,
/// 2026-05-14) confirmed that <see cref="PostgresException.TableName"/>,
/// <see cref="PostgresException.ConstraintName"/>, and
/// <see cref="PostgresException.ColumnName"/> all populate cleanly for these three
/// SqlStates — unlike RLS 42501 which leaves <c>TableName</c> empty and required
/// message-text parsing in 7.6.2. The translator reads the structured fields
/// directly with a defensive null-guard.
/// </para>
/// </summary>
public class DbExceptionTranslatorTests
{
    [Fact]
    public void Translates_23505_to_UniqueConstraintViolationException_with_constraint_name()
    {
        var pg = MakePostgresException("23505", tableName: "Accounts", constraintName: "IX_Accounts_Name_UserId");
        var wrapped = new DbUpdateException("oh no", pg);

        var translated = DbExceptionTranslator.TryTranslate(wrapped, out var result);

        translated.Should().BeTrue();
        result.Should().BeOfType<UniqueConstraintViolationException>();
        var unique = (UniqueConstraintViolationException)result!;
        unique.ConstraintName.Should().Be("IX_Accounts_Name_UserId");
        unique.TableName.Should().Be("Accounts");
        unique.OriginalException.Should().BeSameAs(pg);
    }

    [Fact]
    public void Translates_23503_to_ForeignKeyViolationException_with_constraint_name()
    {
        var pg = MakePostgresException("23503", tableName: "Transactions", constraintName: "FK_Transactions_Categories_CategoryId");
        var wrapped = new DbUpdateException("oh no", pg);

        var translated = DbExceptionTranslator.TryTranslate(wrapped, out var result);

        translated.Should().BeTrue();
        result.Should().BeOfType<ForeignKeyViolationException>();
        var fk = (ForeignKeyViolationException)result!;
        fk.ConstraintName.Should().Be("FK_Transactions_Categories_CategoryId");
        fk.TableName.Should().Be("Transactions");
        fk.OriginalException.Should().BeSameAs(pg);
    }

    [Fact]
    public void Translates_23502_to_NullConstraintViolationException_with_column_name()
    {
        var pg = MakePostgresException("23502", tableName: "Accounts", columnName: "Name");
        var wrapped = new DbUpdateException("oh no", pg);

        var translated = DbExceptionTranslator.TryTranslate(wrapped, out var result);

        translated.Should().BeTrue();
        result.Should().BeOfType<NullConstraintViolationException>();
        var notNull = (NullConstraintViolationException)result!;
        notNull.ColumnName.Should().Be("Name");
        notNull.TableName.Should().Be("Accounts");
        notNull.OriginalException.Should().BeSameAs(pg);
    }

    [Fact]
    public void Leaves_42501_alone_so_RlsExceptionTranslator_handles_it()
    {
        // RLS rejection (42501) is handled by RlsExceptionTranslator (Stage 7.6.2). The
        // catch-chain in AppDbContext.SaveChangesAsync runs RLS first; if that returns
        // false, this translator must NOT swallow the 42501 — it might be a missing-GRANT
        // on a non-user-owned table that should propagate as the original PostgresException.
        var pg = MakePostgresException("42501", tableName: "AspNetUsers");
        var wrapped = new DbUpdateException("oh no", pg);

        var translated = DbExceptionTranslator.TryTranslate(wrapped, out var result);

        translated.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void Leaves_unmatched_SqlState_alone()
    {
        // 40001 serialization failure, 53300 too-many-connections, etc. — the translator
        // is intentionally narrow. New codes get explicit branches as services need them.
        var pg = MakePostgresException("40001", tableName: "Accounts");
        var wrapped = new DbUpdateException("oh no", pg);

        var translated = DbExceptionTranslator.TryTranslate(wrapped, out var result);

        translated.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void Leaves_DbUpdateException_without_PostgresException_alone()
    {
        var wrapped = new DbUpdateException("not a pg error", new InvalidOperationException("not pg"));

        var translated = DbExceptionTranslator.TryTranslate(wrapped, out var result);

        translated.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void Translates_23505_when_constraint_name_is_empty_falling_back_to_null()
    {
        // Defensive: Postgres protocol docs say "frontends should not assume the presence
        // of any of these fields guarantees the presence of another field." If a future
        // code path emits 23505 without the structured ConstraintName field, the
        // translator still wraps the exception (so service code can catch the typed
        // exception) and reports ConstraintName as null rather than throwing.
        var pg = MakePostgresException("23505", tableName: "Accounts", constraintName: null);
        var wrapped = new DbUpdateException("oh no", pg);

        var translated = DbExceptionTranslator.TryTranslate(wrapped, out var result);

        translated.Should().BeTrue();
        var unique = (UniqueConstraintViolationException)result!;
        unique.ConstraintName.Should().BeNull();
    }

    // --- helpers ---------------------------------------------------------

    private static PostgresException MakePostgresException(
        string sqlState,
        string? tableName = null,
        string? constraintName = null,
        string? columnName = null,
        string? messageText = null)
    {
        var pg = new PostgresException(messageText ?? "test", "ERROR", "ERROR", sqlState);
        SetBackingField(pg, nameof(PostgresException.TableName), tableName);
        SetBackingField(pg, nameof(PostgresException.ConstraintName), constraintName);
        SetBackingField(pg, nameof(PostgresException.ColumnName), columnName);
        return pg;
    }

    private static void SetBackingField(PostgresException pg, string propertyName, string? value)
    {
        if (value is null) return;
        var backing = typeof(PostgresException).GetField(
            $"<{propertyName}>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (backing is not null)
        {
            backing.SetValue(pg, value);
        }
        else
        {
            var prop = typeof(PostgresException).GetProperty(propertyName);
            prop?.SetValue(pg, value);
        }
    }
}
