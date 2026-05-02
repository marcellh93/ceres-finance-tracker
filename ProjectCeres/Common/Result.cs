namespace ProjectCeres.Common;

public readonly record struct ResultError(string Code, string Message);

public readonly struct Result
{
    public bool IsSuccess { get; }
    public ResultError? Error { get; }

    private Result(bool ok, ResultError? error)
    {
        IsSuccess = ok;
        Error = error;
    }

    public static Result Ok() => new(true, null);
    public static Result Fail(string code, string message) => new(false, new ResultError(code, message));
}

public readonly struct Result<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public ResultError? Error { get; }

    private Result(bool ok, T? value, ResultError? error)
    {
        IsSuccess = ok;
        Value = value;
        Error = error;
    }

    public static Result<T> Ok(T value) => new(true, value, null);
    public static Result<T> Fail(string code, string message) => new(false, default, new ResultError(code, message));
}
