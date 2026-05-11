namespace ProjectCeres.Common;

public sealed class UserScope : IUserScope
{
    private static readonly AsyncLocal<Guid?> _current = new();

    public Guid? Current => _current.Value;

    public IDisposable EnterAs(Guid userId)
    {
        var previous = _current.Value;
        _current.Value = userId;
        return new ScopeReleaser(previous);
    }

    private sealed class ScopeReleaser(Guid? previous) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _current.Value = previous;
        }
    }
}
