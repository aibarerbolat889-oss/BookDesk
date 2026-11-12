namespace BookDesk;

public sealed record DomainError(string Code, string Message);

public abstract record Result<T>
{
    private Result() { }

    public sealed record Success(T Value) : Result<T>;
    public sealed record Failure(DomainError Error) : Result<T>;
}

public abstract record Option<T>
{
    private Option() { }

    public sealed record Found(T Value) : Option<T>;
    public sealed record Missing(string Key) : Option<T>;
}

public static class OutcomeExtensions
{
    public static Result<U> Map<T, U>(this Result<T> result, Func<T, U> map) =>
        result.Bind(value => new Result<U>.Success(map(value)));

    public static Result<U> Bind<T, U>(this Result<T> result, Func<T, Result<U>> next) =>
        result switch
        {
            Result<T>.Success(var value) => next(value),
            Result<T>.Failure(var error) => new Result<U>.Failure(error),
            _ => throw new InvalidOperationException("Unknown result case.")
        };

    public static Option<T> Find<T>(this IEnumerable<T> items, string key, Func<T, string> keyOf)
    {
        foreach (var item in items)
            if (keyOf(item) == key)
                return new Option<T>.Found(item);
        return new Option<T>.Missing(key);
    }

    public static Result<T> Require<T>(this Option<T> option, string code, string entity) =>
        option switch
        {
            Option<T>.Found(var value) => new Result<T>.Success(value),
            Option<T>.Missing(var key) => Fail<T>(code, $"{entity} {key} was not found."),
            _ => throw new InvalidOperationException("Unknown lookup case.")
        };

    public static Result<T> Fail<T>(string code, string message) =>
        new Result<T>.Failure(new DomainError(code, message));
}
