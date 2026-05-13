using System.Data;
using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using ProjectCeres.Common;
using ProjectCeres.Tests.Integration;

namespace ProjectCeres.Tests.Common;

public class RowLevelSecurityInterceptorTests
{
    private const string OriginalSql = "SELECT * FROM \"Transactions\"";

    [Fact]
    public void ReaderExecuting_prepends_SET_LOCAL_with_resolved_user_id()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var (sut, _) = MakeSut(userId);
        var cmd = new FakeDbCommand { CommandText = OriginalSql };

        sut.ReaderExecuting(cmd, FakeEventData(), default);

        cmd.CommandText.Should().Be(
            $"SET LOCAL \"app.current_user_ref\" = '{userId:D}'; {OriginalSql}");
    }

    [Fact]
    public async Task ReaderExecutingAsync_prepends_SET_LOCAL_with_resolved_user_id()
    {
        var userId = Guid.NewGuid();
        var (sut, _) = MakeSut(userId);
        var cmd = new FakeDbCommand { CommandText = OriginalSql };

        await sut.ReaderExecutingAsync(cmd, FakeEventData(), default);

        cmd.CommandText.Should().StartWith($"SET LOCAL \"app.current_user_ref\" = '{userId:D}';");
    }

    [Fact]
    public void NonQueryExecuting_prepends_SET_LOCAL()
    {
        var userId = Guid.NewGuid();
        var (sut, _) = MakeSut(userId);
        var cmd = new FakeDbCommand { CommandText = "UPDATE \"Transactions\" SET \"Amount\" = 1" };

        sut.NonQueryExecuting(cmd, FakeEventData(), default);

        cmd.CommandText.Should().StartWith("SET LOCAL \"app.current_user_ref\" = ");
    }

    [Fact]
    public void ScalarExecuting_prepends_SET_LOCAL()
    {
        var userId = Guid.NewGuid();
        var (sut, _) = MakeSut(userId);
        var cmd = new FakeDbCommand { CommandText = "SELECT COUNT(*) FROM \"Transactions\"" };

        sut.ScalarExecuting(cmd, FakeEventData(), default);

        cmd.CommandText.Should().StartWith("SET LOCAL \"app.current_user_ref\" = ");
    }

    [Fact]
    public void Skips_SET_LOCAL_when_user_is_empty_and_preauth_is_tagged_and_does_not_warn()
    {
        var (sut, logs) = MakeSut(Guid.Empty, isPreAuth: true);
        var cmd = new FakeDbCommand { CommandText = OriginalSql };

        sut.ReaderExecuting(cmd, FakeEventData(), default);

        cmd.CommandText.Should().Be(OriginalSql, "pre-auth path leaves the command untouched");
        logs.Should().NotContain(l => l.Contains("Warning"));
    }

    [Fact]
    public void Logs_warning_when_user_is_empty_outside_preauth_paths()
    {
        var (sut, logs) = MakeSut(Guid.Empty, isPreAuth: false);
        var cmd = new FakeDbCommand { CommandText = OriginalSql };

        sut.ReaderExecuting(cmd, FakeEventData(), default);

        cmd.CommandText.Should().Be(OriginalSql, "still no SET LOCAL on Guid.Empty");
        logs.Should().Contain(l => l.Contains("Warning") && l.Contains("Guid.Empty"));
    }

    [Fact]
    public void Never_throws_on_Guid_Empty()
    {
        // Pins option A: the interceptor is the safety-net, not the doorway.
        // Doorway refusal lives in BackgroundJobScope (Commit 6).
        var (sut, _) = MakeSut(Guid.Empty, isPreAuth: false);
        var cmd = new FakeDbCommand { CommandText = OriginalSql };

        var act = () => sut.ReaderExecuting(cmd, FakeEventData(), default);

        act.Should().NotThrow();
    }

    [Fact]
    public void Returns_base_interception_result_unchanged()
    {
        // The interceptor's job is to mutate command text, not to short-circuit EF.
        // Returning InterceptionResult.SuppressWithResult would cancel the real query.
        var (sut, _) = MakeSut(Guid.NewGuid());
        var cmd = new FakeDbCommand { CommandText = OriginalSql };

        var result = sut.ReaderExecuting(cmd, FakeEventData(), default);

        result.HasResult.Should().BeFalse("EF must run the underlying command, not be suppressed");
    }

    // --- helpers ---------------------------------------------------------

    private static (RowLevelSecurityInterceptor Sut, List<string> Logs) MakeSut(
        Guid userId, bool isPreAuth = false)
    {
        var user = new FakeCurrentUserAccessor(userId);

        var tagger = new Mock<IPreAuthCallSiteTagger>();
        tagger.Setup(t => t.IsLegitimatePreAuth()).Returns(isPreAuth);

        var logs = new List<string>();
        using var lf = LoggerFactory.Create(b => b.AddProvider(new InMemoryLoggerProvider(logs)));
        var logger = lf.CreateLogger<RowLevelSecurityInterceptor>();

        return (new RowLevelSecurityInterceptor(user, tagger.Object, logger), logs);
    }

    private static CommandEventData FakeEventData()
    {
        // The interceptor only needs the DbCommand argument; CommandEventData
        // is constructed with the minimal surface required by the abstract base.
        return new CommandEventData(
            eventDefinition: null!,
            messageGenerator: (d, _) => d.ToString() ?? string.Empty,
            connection: null!,
            command: null!,
            logCommandText: string.Empty,
            context: null,
            executeMethod: DbCommandMethod.ExecuteReader,
            commandId: Guid.NewGuid(),
            connectionId: Guid.NewGuid(),
            async: false,
            logParameterValues: false,
            startTime: DateTimeOffset.UtcNow,
            commandSource: CommandSource.Unknown);
    }

    // Minimal DbCommand test double. We only need the CommandText property
    // mutation to be observable; the rest is plumbing the abstract base requires.
    private sealed class FakeDbCommand : DbCommand
    {
        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection { get; } = new FakeParamCollection();
        protected override DbTransaction? DbTransaction { get; set; }
        public override void Cancel() { }
        public override int ExecuteNonQuery() => 0;
        public override object? ExecuteScalar() => null;
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            throw new NotSupportedException();
    }

    private sealed class FakeParamCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _list = new();
        public override int Count => _list.Count;
        public override object SyncRoot => this;
        public override int Add(object value) { _list.Add((DbParameter)value); return _list.Count - 1; }
        public override void AddRange(Array values) { foreach (var v in values) _list.Add((DbParameter)v); }
        public override void Clear() => _list.Clear();
        public override bool Contains(object value) => _list.Contains(value);
        public override bool Contains(string value) => false;
        public override void CopyTo(Array array, int index) { }
        public override System.Collections.IEnumerator GetEnumerator() => _list.GetEnumerator();
        protected override DbParameter GetParameter(int index) => _list[index];
        protected override DbParameter GetParameter(string parameterName) => throw new NotSupportedException();
        public override int IndexOf(object value) => _list.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName) => -1;
        public override void Insert(int index, object value) => _list.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _list.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _list.RemoveAt(index);
        public override void RemoveAt(string parameterName) { }
        protected override void SetParameter(int index, DbParameter value) => _list[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value) { }
    }
}
