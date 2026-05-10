using Microsoft.Extensions.Logging;

namespace ProjectCeres.Tests.Integration;

/// <summary>
/// A minimal ILoggerProvider that appends every formatted log message to an
/// in-memory list. Used in tests that assert no PII reaches any logger.
/// </summary>
public sealed class InMemoryLoggerProvider : ILoggerProvider
{
    private readonly List<string> _sink;

    public InMemoryLoggerProvider(List<string> sink) => _sink = sink;

    public ILogger CreateLogger(string categoryName) =>
        new InMemoryLogger(_sink, categoryName);

    public void Dispose() { }

    private sealed class InMemoryLogger : ILogger
    {
        private readonly List<string> _sink;
        private readonly string _category;

        public InMemoryLogger(List<string> sink, string category)
        {
            _sink = sink;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            lock (_sink)
            {
                _sink.Add($"[{_category}] {logLevel}: {message}");
            }
        }
    }
}
