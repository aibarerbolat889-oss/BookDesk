namespace BookDesk.Tests;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class TestCaseAttribute : Attribute;

internal static class Assert
{
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
    }

    public static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException("The actual sequence differs from the expected sequence.");
    }

    public static void True(bool actual) => Equal(true, actual);
    public static void False(bool actual) => Equal(false, actual);
    public static void Empty<T>(IEnumerable<T> items) => False(items.Any());
    public static void DoesNotContain<T>(IEnumerable<T> items, Func<T, bool> predicate) => False(items.Any(predicate));
    public static void EndsWith(string suffix, string actual) => True(actual.EndsWith(suffix, StringComparison.Ordinal));

    public static T IsType<T>(object actual)
    {
        if (actual.GetType() == typeof(T))
            return (T)actual;
        throw new InvalidOperationException($"Expected '{typeof(T).Name}', received '{actual.GetType().Name}'.");
    }

    public static T Single<T>(IEnumerable<T> items)
    {
        using var iterator = items.GetEnumerator();
        True(iterator.MoveNext());
        var item = iterator.Current;
        False(iterator.MoveNext());
        return item;
    }

    public static void All<T>(IEnumerable<T> items, Action<T> check)
    {
        foreach (var item in items)
            check(item);
    }
}
