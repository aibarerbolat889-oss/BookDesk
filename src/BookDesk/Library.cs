using static BookDesk.OutcomeExtensions;

namespace BookDesk;

public sealed record Change(LibraryState State, string Message);
public sealed record CheckoutRequest(string BookId, string MemberId, DateOnly Today);
public sealed record FeeInput(int DaysLate, int RatePerDay, int GraceDays, int Cap);

public static class LendingPolicy
{
    public const int BalanceLimit = 2_000;
    public const int RenewalDays = 7;

    public static Func<DateOnly, DateOnly> DueAfter(int days) => opened => opened.AddDays(days);
    public static Func<DateOnly, DateOnly> StudentDue { get; } = DueAfter(14);
    public static Func<DateOnly, DateOnly> StaffDue { get; } = DueAfter(28);

    public static FeeInput StandardFee(int daysLate) => new(daysLate, 100, 0, 3_000);
    public static FeeInput GraceFee(int daysLate) => new(daysLate, 100, 2, 3_000);

    public static Result<int> AssessFee(Loan loan, DateOnly returned,
        Func<int, FeeInput> policy, Func<FeeInput, Result<int>> calculate)
    {
        var daysLate = Math.Max(0, returned.DayNumber - loan.Due.DayNumber);
        return calculate(policy(daysLate));
    }
}

public static class Library
{
    private sealed record CheckoutContext(CheckoutRequest Request, Book Book, Member Member);

    public static Result<Change> Checkout(LibraryState state, CheckoutRequest request) =>
        ResolveCheckout(state, request)
            .Bind(context => CheckEligibility(state, context))
            .Bind(CreateCheckoutLoan)
            .Bind(loan => RegisterCheckout(state, loan));

    private static Result<CheckoutContext> ResolveCheckout(LibraryState state, CheckoutRequest request) =>
        state.Books.Find(ModelRules.Key(request.BookId), book => book.Id).Require("book-missing", "Book")
            .Bind(book => state.Members.Find(ModelRules.Key(request.MemberId), member => member.Id)
                .Require("member-missing", "Member")
                .Map(member => new CheckoutContext(request, book, member)));

    private static Result<CheckoutContext> CheckEligibility(LibraryState state, CheckoutContext context)
    {
        var (_, book, member) = context;
        var active = state.Loans.Find(book.Id, loan => loan.BookId);
        var first = state.Holds.Find(book.Id, hold => hold.BookId);
        return (active, first, member.Balance) switch
        {
            (Option<Loan>.Found, _, _) =>
                Fail<CheckoutContext>("book-on-loan", $"{book.Id} already has an active loan."),
            (_, Option<Hold>.Found(var hold), _) when hold.MemberId != member.Id =>
                Fail<CheckoutContext>("hold-priority", $"{book.Id} is reserved for the first member in its queue."),
            (_, _, >= LendingPolicy.BalanceLimit) =>
                Fail<CheckoutContext>("balance-limit", $"Clear enough of the balance to fall below {LendingPolicy.BalanceLimit} KZT."),
            _ when state.Loans.Count(loan => loan.MemberId == member.Id) >= member.LoanLimit =>
                Fail<CheckoutContext>("loan-limit", $"{member.Id} has reached the active loan limit."),
            _ => new Result<CheckoutContext>.Success(context)
        };
    }

    private static Result<Loan> CreateCheckoutLoan(CheckoutContext context)
    {
        var (request, book, member) = context;
        if (!ModelRules.IsDate(request.Today))
            return Fail<Loan>("invalid-date", "The checkout date must be between 2000 and 2100.");
        var calculateDue = member.Kind == MemberKind.Student ? LendingPolicy.StudentDue : LendingPolicy.StaffDue;
        return Loan.Create(book.Id, member.Id, request.Today, calculateDue(request.Today), 0);
    }

    private static Result<Change> RegisterCheckout(LibraryState state, Loan loan) =>
        LibraryState.Create(state.Books, state.Members, state.Loans.Add(loan),
            state.Holds.Where(hold => (hold.BookId, hold.MemberId) != (loan.BookId, loan.MemberId)))
            .Map(next => new Change(next, FormattableString.Invariant(
                $"Checked out {loan.BookId} to {loan.MemberId}. Due {loan.Due:yyyy-MM-dd}.")));

    public static Result<Change> Return(LibraryState state, string bookId, DateOnly today,
        Func<FeeInput, Result<int>> calculateFee) =>
        state.Loans.Find(ModelRules.Key(bookId), loan => loan.BookId).Require("loan-missing", "Active loan")
            .Bind(loan => ValidateLoanDate(loan, today))
            .Bind(loan => state.Members.Find(loan.MemberId, member => member.Id).Require("member-missing", "Member")
                .Bind(member =>
                {
                    Func<int, FeeInput> policy = member.Kind == MemberKind.Student
                        ? LendingPolicy.StandardFee : LendingPolicy.GraceFee;
                    return LendingPolicy.AssessFee(loan, today, policy, calculateFee)
                        .Bind(fee => RegisterReturn(state, loan, member, fee, policy));
                }));

    private static Result<Change> RegisterReturn(LibraryState state, Loan loan, Member member, int fee,
        Func<int, FeeInput> policy)
    {
        if (fee < 0 || fee > policy(0).Cap)
            return Fail<Change>("invalid-fee", "The calculator returned a fee outside the policy range.");

        return Member.Create(member.Id, member.Name, member.Kind, member.Balance + fee)
            .Bind(updated => LibraryState.Create(state.Books, state.Members.Replace(member, updated),
                state.Loans.Remove(loan), state.Holds))
            .Map(next => new Change(next, $"Returned {loan.BookId}. Fee {fee} KZT. Balance {member.Balance + fee} KZT."));
    }

    public static Result<Change> Renew(LibraryState state, string bookId, DateOnly today) =>
        state.Loans.Find(ModelRules.Key(bookId), loan => loan.BookId).Require("loan-missing", "Active loan")
            .Bind(loan => ValidateLoanDate(loan, today))
            .Bind(loan => loan switch
            {
                { Renewals: >= 1 } => Fail<Loan>("renewal-limit", "A loan can be renewed only once."),
                _ when loan.Due < today => Fail<Loan>("loan-overdue", "An overdue loan cannot be renewed."),
                _ when state.Holds.Any(hold => hold.BookId == loan.BookId) =>
                    Fail<Loan>("hold-waiting", "A queued hold blocks renewal."),
                _ => Loan.Create(loan.BookId, loan.MemberId, loan.Opened,
                    loan.Due.AddDays(LendingPolicy.RenewalDays), loan.Renewals + 1)
            })
            .Bind(updated => LibraryState.Create(state.Books, state.Members,
                state.Loans.Select(loan => loan.BookId == updated.BookId ? updated : loan), state.Holds)
                .Map(next => new Change(next,
                    FormattableString.Invariant($"Renewed {updated.BookId}. Due {updated.Due:yyyy-MM-dd}."))));

    public static Result<Change> PlaceHold(LibraryState state, string bookId, string memberId) =>
        Hold.Create(bookId, memberId)
            .Bind(hold => state.Books.Find(hold.BookId, book => book.Id).Require("book-missing", "Book")
                .Bind(_ => state.Members.Find(hold.MemberId, member => member.Id).Require("member-missing", "Member"))
                .Bind(_ => CheckHold(state, hold)))
            .Bind(hold => LibraryState.Create(state.Books, state.Members, state.Loans, state.Holds.Add(hold))
                .Map(next => new Change(next,
                    $"Hold placed for {hold.MemberId} on {hold.BookId}. Queue position {next.Holds.Count(item => item.BookId == hold.BookId)}.")));

    private static Result<Hold> CheckHold(LibraryState state, Hold hold)
    {
        var active = state.Loans.Find(hold.BookId, loan => loan.BookId);
        var waiting = state.Holds.Where(item => item.BookId == hold.BookId).ToArray();
        return active switch
        {
            Option<Loan>.Found(var loan) when loan.MemberId == hold.MemberId =>
                Fail<Hold>("borrower-hold", "The current borrower cannot hold their own book."),
            _ when waiting.Any(item => item.MemberId == hold.MemberId) =>
                Fail<Hold>("duplicate-hold", "The member already has a hold for this book."),
            Option<Loan>.Missing when waiting.Length == 0 =>
                Fail<Hold>("book-available", "The book is available for immediate checkout."),
            _ => new Result<Hold>.Success(hold)
        };
    }

    public static Result<Change> Pay(LibraryState state, string memberId, long amount) =>
        state.Members.Find(ModelRules.Key(memberId), member => member.Id).Require("member-missing", "Member")
            .Bind(member => amount > 0 && amount <= member.Balance
                ? Member.Create(member.Id, member.Name, member.Kind, member.Balance - amount)
                    .Bind(updated => LibraryState.Create(state.Books, state.Members.Replace(member, updated),
                        state.Loans, state.Holds))
                    .Map(next => new Change(next, $"Payment {amount} KZT from {member.Id}. Balance {member.Balance - amount} KZT."))
                : Fail<Change>("invalid-payment", "Payment must be positive and cannot exceed the balance."));

    private static Result<Loan> ValidateLoanDate(Loan loan, DateOnly today) =>
        ModelRules.IsDate(today) && today >= loan.Opened
            ? new Result<Loan>.Success(loan)
            : Fail<Loan>("invalid-date", "The operation date must be in range and on or after checkout.");
}
