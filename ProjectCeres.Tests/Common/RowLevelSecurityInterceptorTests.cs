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
    [Fact]
    public async Task ConnectionOpenedAsync_issues_set_config_with_resolved_user_id()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var (sut, _) = MakeSut(userId);
        var conn = new RecordingConnection();

        await sut.ConnectionOpenedAsync(conn, FakeEventData(), default);

        conn.ExecutedCommands.Should().ContainSingle()
            .Which.Should().Be($"SELECT set_config('app.current_user_ref', '{userId:D}', false)");
    }

    [Fact]
    public void ConnectionOpened_sync_issues_set_config_with_resolved_user_id()
    {
        var userId = Guid.NewGuid();
        var (sut, _) = MakeSut(userId);
        var conn = new RecordingConnection();

        sut.ConnectionOpened(conn, FakeEventData());

        conn.ExecutedCommands.Should().ContainSingle()
            .Which.Should().StartWith("SELECT set_config('app.current_user_ref', ");
    }

    [Fact]
    public async Task ConnectionOpenedAsync_resets_GUC_on_pre_auth_path_without_warning()
    {
        var (sut, logs) = MakeSut(Guid.Empty, isPreAuth: true);
        var conn = new RecordingConnection();

        await sut.ConnectionOpenedAsync(conn, FakeEventData(), default);

        conn.ExecutedCommands.Should().ContainSingle()
            .Which.Should().Be("RESET \"app.current_user_ref\"");
        logs.Should().NotContain(l => l.Contains("Warning"));
    }

    [Fact]
    public async Task ConnectionOpenedAsync_resets_GUC_outside_pre_auth_with_warning()
    {
        var (sut, logs) = MakeSut(Guid.Empty, isPreAuth: false);
        var conn = new RecordingConnection();

        await sut.ConnectionOpenedAsync(conn, FakeEventData(), default);

        conn.ExecutedCommands.Should().ContainSingle()
            .Which.Should().Be("RESET \"app.current_user_ref\"");
        logs.Should().Contain(l => l.Contains("Warning") && l.Contains("Guid.Empty"));
    }

    [Fact]
    public async Task ConnectionOpenedAsync_never_throws_on_Guid_Empty()
    {
        // Pins option A: the interceptor is the safety-net, not the doorway.
        // Doorway refusal lives in BackgroundJobScope (Commit 6).
        var (sut, _) = MakeSut(Guid.Empty, isPreAuth: false);
        var conn = new RecordingConnection();

        Func<Task> act = () => sut.ConnectionOpenedAsync(conn, FakeEventData(), default);

        await act.Should().NotThrowAsync();
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

    private static ConnectionEndEventData FakeEventData() =>
        new(
            eventDefinition: null!,
            messageGenerator: (d, _) => d.ToString() ?? string.Empty,
            connection: null!,
            context: null,
            connectionId: Guid.NewGuid(),
            async: false,
            startTime: DateTimeOffset.UtcNow,
            duration: TimeSpan.Zero);

    // Minimal DbConnection that records every CommandText executed against it.
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
        protected override DbCommand CreateDbCommand() => new FakeDbCommand(this);
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
        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            _connection.ExecutedCommands.Add(CommandText);
            return Task.FromResult(0);
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
