using System.Collections.Immutable;
using static BookDesk.OutcomeExtensions;

namespace BookDesk;

public enum MemberKind { Student, Staff }

public static class ModelRules
{
    public static string Key(string? text) => (text ?? "").Trim().ToUpperInvariant();

    public static bool IsKey(string value) =>
        value.Length is > 0 and <= 12 &&
        value.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9');

    public static bool IsLabel(string value) =>
        value.Length is > 0 and <= 100 && !value.Any(char.IsControl);

    public static bool IsDate(DateOnly date) => date.Year is >= 2000 and <= 2100;
}

public sealed record Book
{
    private Book(string id, string title, string category) =>
        (Id, Title, Category) = (id, title, category);

    public string Id { get; }
    public string Title { get; }
    public string Category { get; }

    public static Result<Book> Create(string? id, string? title, string? category)
    {
        var key = ModelRules.Key(id);
        var name = (title ?? "").Trim();
        var group = (category ?? "").Trim();
        return ModelRules.IsKey(key) && ModelRules.IsLabel(name) && ModelRules.IsLabel(group)
            ? new Result<Book>.Success(new Book(key, name, group))
            : Fail<Book>("invalid-book", "A book needs a valid ID, title and category.");
    }
}

public sealed record Member
{
    private Member(string id, string name, MemberKind kind, long balance) =>
        (Id, Name, Kind, Balance) = (id, name, kind, balance);

    public string Id { get; }
    public string Name { get; }
    public MemberKind Kind { get; }
    public long Balance { get; }
    public int LoanLimit => Kind == MemberKind.Student ? 3 : 5;

    public static Result<Member> Create(string? id, string? name, MemberKind kind, long balance)
    {
        var key = ModelRules.Key(id);
        var label = (name ?? "").Trim();
        return ModelRules.IsKey(key) && ModelRules.IsLabel(label) &&
               Enum.IsDefined(kind) && balance is >= 0 and <= int.MaxValue
            ? new Result<Member>.Success(new Member(key, label, kind, balance))
            : Fail<Member>("invalid-member", "A member needs a valid ID, name, kind and balance.");
    }
}

public sealed record Loan
{
    private Loan(string bookId, string memberId, DateOnly opened, DateOnly due, int renewals) =>
        (BookId, MemberId, Opened, Due, Renewals) = (bookId, memberId, opened, due, renewals);

    public string BookId { get; }
    public string MemberId { get; }
    public DateOnly Opened { get; }
    public DateOnly Due { get; }
    public int Renewals { get; }

    public static Result<Loan> Create(string? bookId, string? memberId, DateOnly opened, DateOnly due, int renewals)
    {
        var book = ModelRules.Key(bookId);
        var member = ModelRules.Key(memberId);
        return ModelRules.IsKey(book) && ModelRules.IsKey(member) && ModelRules.IsDate(opened) &&
               ModelRules.IsDate(due) && due > opened && renewals is 0 or 1
            ? new Result<Loan>.Success(new Loan(book, member, opened, due, renewals))
            : Fail<Loan>("invalid-loan", "A loan needs valid IDs, ordered dates and zero or one renewals.");
    }
}

public sealed record Hold
{
    private Hold(string bookId, string memberId) => (BookId, MemberId) = (bookId, memberId);

    public string BookId { get; }
    public string MemberId { get; }

    public static Result<Hold> Create(string? bookId, string? memberId)
    {
        var book = ModelRules.Key(bookId);
        var member = ModelRules.Key(memberId);
        return ModelRules.IsKey(book) && ModelRules.IsKey(member)
            ? new Result<Hold>.Success(new Hold(book, member))
            : Fail<Hold>("invalid-hold", "A hold needs valid book and member IDs.");
    }
}

public sealed class LibraryState
{
    private LibraryState(ImmutableArray<Book> books, ImmutableArray<Member> members,
                         ImmutableArray<Loan> loans, ImmutableArray<Hold> holds) =>
        (Books, Members, Loans, Holds) = (books, members, loans, holds);

    public ImmutableArray<Book> Books { get; }
    public ImmutableArray<Member> Members { get; }
    public ImmutableArray<Loan> Loans { get; }
    public ImmutableArray<Hold> Holds { get; }

    public static Result<LibraryState> Create(IEnumerable<Book> books, IEnumerable<Member> members,
        IEnumerable<Loan> loans, IEnumerable<Hold> holds)
    {
        var inventory = books.ToImmutableArray();
        var patrons = members.ToImmutableArray();
        var active = loans.ToImmutableArray();
        var queue = holds.ToImmutableArray();
        if (inventory.Any(book => book is null) || patrons.Any(member => member is null) ||
            active.Any(loan => loan is null) || queue.Any(hold => hold is null))
            return Fail<LibraryState>("invalid-state", "Library collections cannot contain null entries.");

        var bookIds = inventory.Select(book => book.Id).ToHashSet(StringComparer.Ordinal);
        var memberIds = patrons.Select(member => member.Id).ToHashSet(StringComparer.Ordinal);
        if (bookIds.Count != inventory.Length || memberIds.Count != patrons.Length)
            return Fail<LibraryState>("invalid-state", "Book and member IDs must be unique.");
        if (active.Select(loan => loan.BookId).Distinct().Count() != active.Length ||
            active.Any(loan => !bookIds.Contains(loan.BookId) || !memberIds.Contains(loan.MemberId)))
            return Fail<LibraryState>("invalid-state", "Active loans must have unique books and existing members.");
        if (patrons.Any(member => active.Count(loan => loan.MemberId == member.Id) > member.LoanLimit))
            return Fail<LibraryState>("invalid-state", "A member exceeds their active loan limit.");
        var borrowers = active.Select(loan => (loan.BookId, loan.MemberId)).ToHashSet();
        if (queue.Select(hold => (hold.BookId, hold.MemberId)).Distinct().Count() != queue.Length ||
            queue.Any(hold => !bookIds.Contains(hold.BookId) || !memberIds.Contains(hold.MemberId) ||
                              borrowers.Contains((hold.BookId, hold.MemberId))))
            return Fail<LibraryState>("invalid-state", "Holds must be unique, refer to existing records and exclude borrowers.");
        return new Result<LibraryState>.Success(new LibraryState(inventory, patrons, active, queue));
    }
}
