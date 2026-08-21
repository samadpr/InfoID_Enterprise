namespace InfoID.Shared.Common;

/// <summary>
/// Standard outcome envelope for Application-layer service calls. Lets a
/// ViewModel handle "didn't work" (validation failure, business rule
/// violation) without a try/catch around every call, while still using real
/// exceptions for things that ARE exceptional (bugs, DB connection loss).
/// </summary>
public class Result
{
    public bool Succeeded { get; }
    public IReadOnlyList<string> Errors { get; }

    protected Result(bool succeeded, IReadOnlyList<string> errors)
    {
        Succeeded = succeeded;
        Errors = errors;
    }

    public static Result Success() => new(true, Array.Empty<string>());

    public static Result Failure(params string[] errors) => new(false, errors);

    public static Result Failure(IEnumerable<string> errors) => new(false, errors.ToList());
}

/// <summary>Result carrying a return value on success (e.g. the Id of a newly created row).</summary>
public class Result<T> : Result
{
    public T? Value { get; }

    private Result(bool succeeded, T? value, IReadOnlyList<string> errors)
        : base(succeeded, errors)
    {
        Value = value;
    }

    public static Result<T> Success(T value) => new(true, value, Array.Empty<string>());

    public static new Result<T> Failure(params string[] errors) => new(false, default, errors);

    public static new Result<T> Failure(IEnumerable<string> errors) => new(false, default, errors.ToList());
}
