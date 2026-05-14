using System.Data;
using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using ProjectCeres.Common;
using ProjectCeres.Tests.Integration;

namespace ProjectCeres.Tests.Common;

/// <summary>
/// Stage 7.6.7 / ADR-0073: tests now construct <see cref="UserContext"/> cases directly
/// instead of mocking the deleted <c>IPreAuthCallSiteTagger</c>. Each case has a distinct
/// observable: command issued (<c>set_config</c> vs <c>RESET</c>) and log signature
/// (Debug for PreAuth, Error for Background, silent for Resolved/Uninitialized).
/// </summary>
public class RowLevelSecurityInterceptorTests
{
    [Fact]
    public async Task ConnectionOpenedAsync_with_Resolved_issues_set_config_with_user_id()
    {
        var userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var (sut, _) = MakeSut(new UserContext.Resolved(userId));
        var conn = new RecordingConnection();

        await sut.ConnectionOpenedAsync(conn, FakeEventData(), default);

        conn.ExecutedCommands.Should().ContainSingle()
            .Which.Should().Be($"SELECT set_config('app.current_user_ref', '{userId:D}', false)");
    }

    [Fact]
    public void ConnectionOpened_sync_with_Resolved_issues_set_config_with_user_id()
    {
        var userId = Guid.NewGuid();
        var (sut, _) = MakeSut(new UserContext.Resolved(userId));
        var conn = new RecordingConnection();

        sut.ConnectionOpened(conn, FakeEventData());

        conn.ExecutedCommands.Should().ContainSingle()
            .Which.Should().StartWith("SELECT set_config('app.current_user_ref', ");
    }

    [Fact]
    public async Task ConnectionOpenedAsync_with_PreAuth_resets_GUC_and_logs_Debug_only()
    {
        var (sut, logs) = MakeSut(new UserContext.PreAuth("Auth.Login"));
        var conn = new RecordingConnection();

        await sut.ConnectionOpenedAsync(conn, FakeEventData(), default);

        conn.ExecutedCommands.Should().ContainSingle()
            .Which.Should().Be("RESET \"app.current_user_ref\"");
        // Debug log carries the call-site name; no Error / Warning above Debug.
        logs.Should().NotContain(l => l.Contains("Error:") || l.Contains("Warning:"));
        logs.Should().Contain(l => l.Contains("Auth.Login"));
    }

    [Fact]
    public async Task ConnectionOpenedAsync_with_Background_resets_GUC_and_logs_Error_with_reason()
    {
        var reason = "HTTP request reached the DB without auth or [PreAuthCallSite]. Endpoint: /api/foo";
        var (sut, logs) = MakeSut(new UserContext.Background(reason));
        var conn = new RecordingConnection();

        await sut.ConnectionOpenedAsync(conn, FakeEventData(), default);

        conn.ExecutedCommands.Should().ContainSingle()
            .Which.Should().Be("RESET \"app.current_user_ref\"");
        logs.Should().Contain(l => l.Contains("Error:") && l.Contains(reason));
    }

    [Fact]
    public async Task ConnectionOpenedAsync_with_Uninitialized_resets_GUC_silently()
    {
        // EF model-creation case: logging here would be noise — the model creator is
        // supposed to fire eagerly with no ambient context. RESET still happens to
        // prevent leaked GUC bleeding through pooled connections.
        var (sut, logs) = MakeSut(UserContext.Uninitialized.Instance);
        var conn = new RecordingConnection();

        await sut.ConnectionOpenedAsync(conn, FakeEventData(), default);

        conn.ExecutedCommands.Should().ContainSingle()
            .Which.Should().Be("RESET \"app.current_user_ref\"");
        logs.Should().BeEmpty();
    }

    [Fact]
    public async Task ConnectionOpenedAsync_never_throws_for_any_UserContext_case()
    {
        // Pins option A: the interceptor is the safety-net, not the doorway. Doorway
        // refusal lives in BackgroundJobScope.
        UserContext[] cases =
        {
            new UserContext.Resolved(Guid.NewGuid()),
            new UserContext.PreAuth("Some.CallSite"),
            new UserContext.Background("some reason"),
            UserContext.Uninitialized.Instance,
        };

        foreach (var ctx in cases)
        {
            var (sut, _) = MakeSut(ctx);
            var conn = new RecordingConnection();
            Func<Task> act = () => sut.ConnectionOpenedAsync(conn, FakeEventData(), default);
            await act.Should().NotThrowAsync($"the interceptor must never throw for {ctx.GetType().Name}");
        }
    }

    // --- helpers ---------------------------------------------------------

    private static (RowLevelSecurityInterceptor Sut, List<string> Logs) MakeSut(UserContext context)
    {
        var user = new FakeCurrentUserAccessor(context);
        var logs = new List<string>();
        var lf = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Debug);
            b.AddProvider(new InMemoryLoggerProvider(logs));
        });
        var logger = lf.CreateLogger<RowLevelSecurityInterceptor>();
        return (new RowLevelSecurityInterceptor(user, logger), logs);
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
