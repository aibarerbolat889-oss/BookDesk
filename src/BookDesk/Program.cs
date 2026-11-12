using System.Globalization;
using BookDesk;

if (args.Length == 0 || args is ["--help"] or ["-h"])
{
    Console.WriteLine("""
        BookDesk | Campus library lending

        Usage: BookDesk <json-file> <command> [arguments]
        Dates use yyyy-MM-dd. Omitted dates use the computer's local date.

          checkout <book> <member> [date]  Issue an available book
          return   <book> [date]           Return a loan and assess a late fee
          renew    <book> [date]           Extend an eligible loan once
          hold     <book> <member>         Join the queue for a loaned or reserved book
          pay      <member> <tenge>        Apply a payment to a member's balance
          overdue  [date]                 List overdue books and days late
          stock                           Summarize availability by category
        """);
    return 0;
}

if (args.Length < 2)
    return Error("usage", "Supply a JSON path and a command. Use --help for syntax.");

try
{
    var path = Path.GetFullPath(args[0]);
    return args[1..] switch
    {
        ["checkout", var book, var member, .. var date] => WithDate(date,
            today => Update(path, state => Library.Checkout(state, new CheckoutRequest(book, member, today)))),
        ["return", var book, .. var date] => WithDate(date,
            today => Update(path, state => Library.Return(state, book, today, NativeFee.Calculate))),
        ["renew", var book, .. var date] => WithDate(date,
            today => Update(path, state => Library.Renew(state, book, today))),
        ["hold", var book, var member] => Update(path, state => Library.PlaceHold(state, book, member)),
        ["pay", var member, var amount] when long.TryParse(amount, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var tenge) => Update(path, state => Library.Pay(state, member, tenge)),
        ["pay", _, _] => Error("invalid-payment", "Enter an amount in whole tenge."),
        ["overdue", .. var date] => WithDate(date,
            today => Display(LibraryStore.Load(path).Map(state => Reports.Overdue(state, today)), PrintOverdue)),
        ["stock"] => Display(LibraryStore.Load(path).Map(Reports.Availability), PrintStock),
        _ => Error("usage", "Unknown command or wrong argument count. Use --help for syntax.")
    };
}
catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
{
    return Error("path", exception.Message);
}

static int WithDate(string[] arguments, Func<DateOnly, int> action)
{
    if (arguments.Length == 0)
        return action(DateOnly.FromDateTime(DateTime.Today));
    if (arguments is [var date] && DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
        DateTimeStyles.None, out var value) && ModelRules.IsDate(value))
        return action(value);
    return Error("invalid-date", "Supply at most one date in yyyy-MM-dd format between 2000 and 2100.");
}

static int Update(string path, Func<LibraryState, Result<Change>> operation) =>
    Display(LibraryStore.Update(path, operation), change => Console.WriteLine(change.Message));

static int Display<T>(Result<T> result, Action<T> write)
{
    switch (result)
    {
        case Result<T>.Success(var value):
            write(value);
            return 0;
        case Result<T>.Failure(var error):
            return Error(error.Code, error.Message);
        default:
            throw new InvalidOperationException("Unknown result case.");
    }
}

static int Error(string code, string message)
{
    Console.Error.WriteLine($"[{code}] {message}");
    return 1;
}

static void PrintOverdue(OverdueRow[] rows)
{
    Console.WriteLine("OVERDUE LOANS");
    if (rows.Length == 0)
        Console.WriteLine("No overdue loans.");
    foreach (var row in rows)
        Console.WriteLine(FormattableString.Invariant(
            $"{row.BookId} | {row.Title} | {row.MemberId} | {row.Due:yyyy-MM-dd} | {row.DaysLate} day(s) late"));
}

static void PrintStock(CategoryRow[] rows)
{
    Console.WriteLine("CATALOG AVAILABILITY");
    if (rows.Length == 0)
        Console.WriteLine("No books in the catalog.");
    foreach (var row in rows)
        Console.WriteLine($"{row.Category} | Total {row.Total} | On loan {row.OnLoan} | Reserved {row.Reserved} | Available {row.Available}");
}
