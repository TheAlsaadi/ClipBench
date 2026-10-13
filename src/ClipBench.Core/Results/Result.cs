namespace ClipBench.Core.Results;

/// <summary>
/// The outcome of an operation that returns no value. Expected failures are values;
/// exceptions are for bugs. A result that was never initialised is a failure, never a success.
/// </summary>
public readonly record struct Result
{
    private readonly bool _isSuccess;
    private readonly ResultError _error;

    private Result(bool isSuccess, ResultError error)
    {
        _isSuccess = isSuccess;
        _error = error;
    }

    /// <summary>True when the operation succeeded.</summary>
    public bool IsSuccess => _isSuccess;

    /// <summary>True when the operation failed.</summary>
    public bool IsFailure => !_isSuccess;

    /// <summary>Why the operation failed. Reading it from a success is a bug.</summary>
    public ResultError Error => _isSuccess
        ? throw new InvalidOperationException("A successful result has no error.")
        : _error;

    /// <summary>A successful result.</summary>
    public static Result Success() => new(true, default);

    /// <summary>A failed result.</summary>
    public static Result Failure(ResultError error) => new(false, error);

    /// <summary>A successful result that carries a value.</summary>
    public static Result<T> Success<T>(T value) => new(value, default, true);

    /// <summary>A failed result for an operation that would have returned a value.</summary>
    public static Result<T> Failure<T>(ResultError error) => new(default, error, false);

    /// <summary>Describes the outcome without any detail beyond the error code.</summary>
    public override string ToString() => _isSuccess ? "Success" : $"Failure({_error.Code})";
}

/// <summary>The outcome of an operation that returns a value on success.</summary>
/// <typeparam name="T">The type of the value.</typeparam>
public readonly record struct Result<T>
{
    private readonly T? _value;
    private readonly ResultError _error;
    private readonly bool _isSuccess;

    internal Result(T? value, ResultError error, bool isSuccess)
    {
        _value = value;
        _error = error;
        _isSuccess = isSuccess;
    }

    /// <summary>True when the operation succeeded.</summary>
    public bool IsSuccess => _isSuccess;

    /// <summary>True when the operation failed.</summary>
    public bool IsFailure => !_isSuccess;

    /// <summary>The value. Reading it from a failure is a bug.</summary>
    public T Value => _isSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result has no value.");

    /// <summary>Why the operation failed. Reading it from a success is a bug.</summary>
    public ResultError Error => _isSuccess
        ? throw new InvalidOperationException("A successful result has no error.")
        : _error;

    /// <summary>Describes the outcome without the value, which may be clip content.</summary>
    public override string ToString() => _isSuccess ? "Success" : $"Failure({_error.Code})";
}
