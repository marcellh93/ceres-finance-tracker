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
    public void ReaderExecuting_runs_SET_LOCAL_as_a_separate_command_on_the_same_connection()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var (sut, _) = MakeSut(userId);
        var (cmd, connection) = MakeCommandWithConnection(OriginalSql);

        sut.ReaderExecuting(cmd, FakeEventData(), default);

        cmd.CommandText.Should().Be(OriginalSql, "the original command must be untouched");
        connection.ExecutedCommands.Should().ContainSingle()
            .Which.Should().Be($"SET LOCAL \"app.current_user_ref\" = '{userId:D}'");
    }

    [Fact]
    public async Task ReaderExecutingAsync_runs_SET_LOCAL_as_a_separate_command()
    {
        var userId = Guid.NewGuid();
        var (sut, _) = MakeSut(userId);
        var (cmd, connection) = MakeCommandWithConnection(OriginalSql);

        await sut.ReaderExecutingAsync(cmd, FakeEventData(), default);

        connection.ExecutedCommands.Should().ContainSingle()
            .Which.Should().StartWith("SET LOCAL \"app.current_user_ref\" = ");
    }

    [Fact]
    public void NonQueryExecuting_runs_SET_LOCAL_as_a_separate_command()
    {
        var userId = Guid.NewGuid();
        var (sut, _) = MakeSut(userId);
        var (cmd, connection) = MakeCommandWithConnection("UPDATE \"Transactions\" SET \"Amount\" = 1");

        sut.NonQueryExecuting(cmd, FakeEventData(), default);

        connection.ExecutedCommands.Should().ContainSingle()
            .Which.Should().StartWith("SET LOCAL \"app.current_user_ref\" = ");
    }

    [Fact]
    public void ScalarExecuting_runs_SET_LOCAL_as_a_separate_command()
    {
        var userId = Guid.NewGuid();
        var (sut, _) = MakeSut(userId);
        var (cmd, connection) = MakeCommandWithConnection("SELECT COUNT(*) FROM \"Transactions\"");

        sut.ScalarExecuting(cmd, FakeEventData(), default);

        connection.ExecutedCommands.Should().ContainSingle()
            .Which.Should().StartWith("SET LOCAL \"app.current_user_ref\" = ");
    }

    [Fact]
    public void Skips_SET_LOCAL_when_user_is_empty_and_preauth_is_tagged_and_does_not_warn()
    {
        var (sut, logs) = MakeSut(Guid.Empty, isPreAuth: true);
        var (cmd, connection) = MakeCommandWithConnection(OriginalSql);

        sut.ReaderExecuting(cmd, FakeEventData(), default);

        cmd.CommandText.Should().Be(OriginalSql);
        connection.ExecutedCommands.Should().BeEmpty("pre-auth path issues no SET LOCAL");
        logs.Should().NotContain(l => l.Contains("Warning"));
    }

    [Fact]
    public void Logs_warning_when_user_is_empty_outside_preauth_paths()
    {
        var (sut, logs) = MakeSut(Guid.Empty, isPreAuth: false);
        var (cmd, connection) = MakeCommandWithConnection(OriginalSql);

        sut.ReaderExecuting(cmd, FakeEventData(), default);

        cmd.CommandText.Should().Be(OriginalSql);
        connection.ExecutedCommands.Should().BeEmpty("no SET LOCAL when user is empty");
        logs.Should().Contain(l => l.Contains("Warning") && l.Contains("Guid.Empty"));
    }

    [Fact]
    public void Never_throws_on_Guid_Empty()
    {
        // Pins option A: the interceptor is the safety-net, not the doorway.
        // Doorway refusal lives in BackgroundJobScope (Commit 6).
        var (sut, _) = MakeSut(Guid.Empty, isPreAuth: false);
        var (cmd, _) = MakeCommandWithConnection(OriginalSql);

        var act = () => sut.ReaderExecuting(cmd, FakeEventData(), default);

        act.Should().NotThrow();
    }

    [Fact]
    public void Returns_base_interception_result_unchanged()
    {
        // The interceptor's job is to issue SET LOCAL, not short-circuit EF.
        // Returning InterceptionResult.SuppressWithResult would cancel the real query.
        var (sut, _) = MakeSut(Guid.NewGuid());
        var (cmd, _) = MakeCommandWithConnection(OriginalSql);

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

    private static (FakeDbCommand Command, RecordingConnection Connection) MakeCommandWithConnection(
        string commandText)
    {
        var connection = new RecordingConnection();
        var cmd = new FakeDbCommand(connection) { CommandText = commandText };
        return (cmd, connection);
    }

    private static CommandEventData FakeEventData() =>
        new(
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

    // Minimal DbConnection that records every CommandText executed against it.
    // No real database — ExecuteNonQuery returns 0; we assert only on the captured text.
    private sealed class RecordingConnection : DbConnection
    {
        public List<string> ExecutedCommands { get; } = new();

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "test";
        public override string DataSource => "test";
        public override string ServerVersion => "16.0";
        public override ConnectionState State => ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) { }
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();
        protected override DbCommand CreateDbCommand()
        {
            var cmd = new FakeDbCommand(this);
            return cmd;
        }
    }

    private sealed class FakeDbCommand : DbCommand
    {
        private readonly RecordingConnection _connection;

        public FakeDbCommand(RecordingConnection connection) => _connection = connection;

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection
        {
            get => _connection;
            set { /* fixed to the recording connection */ }
        }
        protected override DbParameterCollection DbParameterCollection { get; } = new FakeParamCollection();
        protected override DbTransaction? DbTransaction { get; set; }
        public override void Cancel() { }
        public override int ExecuteNonQuery()
        {
            _connection.ExecutedCommands.Add(CommandText);
            return 0;
        }
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
