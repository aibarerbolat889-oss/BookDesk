namespace BookDesk;

public sealed record OverdueRow(string BookId, string Title, string MemberId, DateOnly Due, int DaysLate);
public sealed record CategoryRow(string Category, int Total, int OnLoan, int Reserved, int Available);

public static class Reports
{
    public static IEnumerable<OverdueRow> StreamOverdue(
        IEnumerable<Loan> loans, IReadOnlyDictionary<string, Book> catalog, DateOnly today)
    {
        foreach (var loan in loans.Where(loan => loan.Due < today))
        {
            yield return new OverdueRow(loan.BookId, catalog[loan.BookId].Title, loan.MemberId,
                loan.Due, today.DayNumber - loan.Due.DayNumber);
        }
    }

    public static OverdueRow[] Overdue(LibraryState state, DateOnly today)
    {
        var catalog = state.Books.ToDictionary(book => book.Id, StringComparer.Ordinal);
        return StreamOverdue(state.Loans, catalog, today)
            .OrderByDescending(row => row.DaysLate)
            .ThenBy(row => row.BookId, StringComparer.Ordinal)
            .ToArray();
    }

    public static CategoryRow[] Availability(LibraryState state)
    {
        var loaned = state.Loans.Select(loan => loan.BookId).ToHashSet(StringComparer.Ordinal);
        var held = state.Holds.Select(hold => hold.BookId).ToHashSet(StringComparer.Ordinal);
        return state.Books.GroupBy(book => book.Category, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CategoryRow(group.Key, group.Count(),
                group.Count(book => loaned.Contains(book.Id)),
                group.Count(book => !loaned.Contains(book.Id) && held.Contains(book.Id)),
                group.Count(book => !loaned.Contains(book.Id) && !held.Contains(book.Id))))
            .OrderByDescending(row => row.OnLoan)
            .ThenBy(row => row.Category, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
