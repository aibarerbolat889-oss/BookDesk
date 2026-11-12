
namespace BookDesk.Tests;

public sealed class LibraryTests
{
    private static readonly DateOnly Today = new(2026, 11, 12);

    [TestCase]
    public void BookFactoryNormalizesKeyAndRejectsBlankTitle()
    {
        var book = Value(Book.Create(" b01 ", " The Hobbit ", "Fiction"));
        Assert.Equal("B01", book.Id);
        Assert.Equal("The Hobbit", book.Title);
        Failure(Book.Create("B01", " ", "Fiction"), "invalid-book");
    }

    [TestCase]
    public void MemberFactoryRejectsNegativeBalanceAndInvalidKind()
    {
        Failure(Member.Create("M1", "Amina", MemberKind.Student, -1), "invalid-member");
        Failure(Member.Create("M1", "Amina", (MemberKind)99, 0), "invalid-member");
    }

    [TestCase]
    public void LoanFactoryRejectsEqualDatesAndSecondRenewal()
    {
        Failure(Loan.Create("B01", "M01", Today, Today, 0), "invalid-loan");
        Failure(Loan.Create("B01", "M01", Today, Today.AddDays(14), 2), "invalid-loan");
    }

    [TestCase]
    public void StateFactoryRejectsDuplicateBooksAndDanglingLoans()
    {
        var state = Sample();
        Failure(LibraryState.Create(state.Books.Add(state.Books[0]), state.Members, state.Loans, state.Holds), "invalid-state");
        Failure(LibraryState.Create([], state.Members, state.Loans, []), "invalid-state");
    }

    [TestCase]
    public void OptionLookupReturnsKeyForMissingRecord()
    {
        var state = Sample();
        Assert.Equal("B01", Assert.IsType<Option<Book>.Found>(state.Books.Find("B01", b => b.Id)).Value.Id);
        Assert.Equal("B99", Assert.IsType<Option<Book>.Missing>(state.Books.Find("B99", b => b.Id)).Key);
    }

    [TestCase]
    public void MapAndBindSkipCallbacksOnFailure()
    {
        var calls = 0;
        var invalid = OutcomeExtensions.Fail<int>("early", "Stop.");
        var outcome = invalid.Map(value => { calls++; return value.ToString(); })
            .Bind(value => { calls++; return new Result<int>.Success(value.Length); });
        Failure(outcome, "early");
        Assert.Equal(0, calls);
    }

    [TestCase]
    public void CurriedDueDatesUseTwoConfiguredTerms()
    {
        Assert.Equal(Today.AddDays(14), LendingPolicy.StudentDue(Today));
        Assert.Equal(Today.AddDays(28), LendingPolicy.StaffDue(Today));
    }

    [TestCase]
    public void CheckoutRejectsDebtAtThresholdAndAllowsOneTengeBelow()
    {
        var state = Sample();
        var request = new CheckoutRequest("B02", "M03", Today);
        Failure(Library.Checkout(state, request), "balance-limit");
        var paid = Value(Library.Pay(state, "M03", 1));
        Assert.Equal(1_999, paid.State.Members.Single(member => member.Id == "M03").Balance);
        Assert.Equal("M03", Value(Library.Checkout(paid.State, request)).State.Loans[^1].MemberId);
    }

    [TestCase]
    public void CheckoutRejectsStudentAtLoanLimit()
    {
        var state = Sample();
        state = Value(Library.Checkout(state, new("B02", "M01", Today))).State;
        state = Value(Library.Checkout(state, new("B04", "M01", Today))).State;
        Failure(Library.Checkout(state, new("B05", "M01", Today)), "loan-limit");
    }

    [TestCase]
    public void RenewOnDueDateAddsSevenDaysAndCannotRepeat()
    {
        var original = Sample();
        var loan = original.Loans.Single(item => item.BookId == "B03");
        var updated = Value(Library.Renew(original, "B03", loan.Due)).State;
        Assert.Equal(loan.Due.AddDays(7), updated.Loans.Single(item => item.BookId == "B03").Due);
        Failure(Library.Renew(updated, "B03", loan.Due), "renewal-limit");
        Failure(Library.Renew(original, "B03", loan.Due.AddDays(1)), "loan-overdue");
    }

    [TestCase]
    public void PaymentRejectsZeroAndExcessAndPermitsExactBalance()
    {
        var state = Sample();
        Failure(Library.Pay(state, "M03", 0), "invalid-payment");
        Failure(Library.Pay(state, "M03", 2_001), "invalid-payment");
        Assert.Equal(0, Value(Library.Pay(state, "M03", 2_000)).State.Members[^1].Balance);
        Assert.Equal(2_000, state.Members[^1].Balance);
    }

    [TestCase]
    public void HoldRejectsAvailableBookBorrowerAndDuplicate()
    {
        var state = Sample();
        Failure(Library.PlaceHold(state, "B02", "M02"), "book-available");
        Failure(Library.PlaceHold(state, "B01", "M01"), "borrower-hold");
        var held = Value(Library.PlaceHold(state, "B01", "M02"));
        Failure(Library.PlaceHold(held.State, "B01", "M02"), "duplicate-hold");
        var reserved = Value(Library.PlaceHold(state, "B03", "M03"));
        Failure(Library.Renew(reserved.State, "B03", Today), "hold-waiting");
    }

    [TestCase]
    public void ReturnWithoutLoanOrBeforeCheckoutNeverCallsNativeAdapter()
    {
        var state = Sample();
        var calls = 0;
        Result<int> Fee(FeeInput input) { calls++; return NativeFee.Calculate(input); }
        Failure(Library.Return(state, "B02", Today, Fee), "loan-missing");
        Failure(Library.Return(state, "B01", Today.AddMonths(-1), Fee), "invalid-date");
        Assert.Equal(0, calls);
        Assert.Equal(2, state.Loans.Length);
    }

    [TestCase]
    public void CheckoutThenRenewKeepsOriginalStateAndUsesMemberPolicy()
    {
        var original = Sample();
        var checkedOut = Value(Library.Checkout(original, new(" b02 ", "m02", Today)));
        Assert.Equal("Checked out B02 to M02. Due 2026-12-10.", checkedOut.Message);
        Failure(Library.Checkout(checkedOut.State, new("B02", "M01", Today)), "book-on-loan");
        var renewed = Value(Library.Renew(checkedOut.State, "B02", Today));
        Assert.Equal("Renewed B02. Due 2026-12-17.", renewed.Message);
        Assert.Equal(1, renewed.State.Loans[^1].Renewals);
        Assert.Equal(0, checkedOut.State.Loans[^1].Renewals);
        Assert.Equal(2, original.Loans.Length);
    }

    [TestCase]
    public void HoldReturnAndCheckoutPreserveQueueOrderAndApplyNativeFee()
    {
        var first = Value(Library.PlaceHold(Sample(), "B01", "M02")).State;
        var queued = Value(Library.PlaceHold(first, "B01", "M03"));
        Assert.EndsWith("Queue position 2.", queued.Message);
        var returned = Value(Library.Return(queued.State, "B01", Today, NativeFee.Calculate));
        Assert.Equal("Returned B01. Fee 300 KZT. Balance 300 KZT.", returned.Message);
        Assert.Equal(1, Reports.Availability(returned.State).Single(row => row.Category == "Fiction").Reserved);
        Failure(Library.Checkout(returned.State, new("B01", "M01", Today)), "hold-priority");
        var checkout = Value(Library.Checkout(returned.State, new("B01", "M02", Today)));
        Assert.Equal("M03", Assert.Single(checkout.State.Holds).MemberId);
        var paid = Value(Library.Pay(checkout.State, "M01", 300));
        Assert.Equal(0, paid.State.Members.Single(member => member.Id == "M01").Balance);
    }

    [TestCase]
    public void NativeCallUsesBothPoliciesOnIdenticalLateDaysAndCapsLargeFees()
    {
        var loan = Sample().Loans[0];
        var returnDate = loan.Due.AddDays(2);
        Assert.Equal(200, Value(LendingPolicy.AssessFee(loan, returnDate, LendingPolicy.StandardFee, NativeFee.Calculate)));
        Assert.Equal(0, Value(LendingPolicy.AssessFee(loan, returnDate, LendingPolicy.GraceFee, NativeFee.Calculate)));
        Assert.Equal(3_000, Value(NativeFee.Calculate(new FeeInput(int.MaxValue, int.MaxValue, 0, 3_000))));
        Assert.Equal(0, Value(NativeFee.Calculate(new FeeInput(0, 100, 0, 3_000))));
    }

    [TestCase]
    public void NativeArgumentRejectionAndAdapterFailureLeaveLoanUnchanged()
    {
        Failure(NativeFee.Calculate(new FeeInput(-1, 100, 0, 3_000)), "native-rejected");
        var state = Sample();
        Failure(Library.Return(state, "B01", Today,
            _ => OutcomeExtensions.Fail<int>("native-unavailable", "Simulated loader failure.")), "native-unavailable");
        Assert.Equal(2, state.Loans.Length);
        Assert.Equal(0, state.Members[0].Balance);
    }

    [TestCase]
    public void ReportsHandleEmptyDataAndNoMatchingDates()
    {
        var empty = Value(LibraryState.Create([], [], [], []));
        Assert.Empty(Reports.Availability(empty));
        Assert.Empty(Reports.Overdue(empty, Today));
        Assert.Empty(Reports.Overdue(Sample(), new DateOnly(2026, 11, 9)));
    }

    [TestCase]
    public void ReportsAggregateStockAndResolveTiesDeterministically()
    {
        var state = Sample();
        var sameDue = Value(Loan.Create("B03", "M02", state.Loans[1].Opened, state.Loans[0].Due, 0));
        var tie = Value(LibraryState.Create(state.Books, state.Members, [sameDue, state.Loans[0]], []));
        var overdue = Reports.Overdue(tie, Today);
        Assert.SequenceEqual(new[] { "B01", "B03" }, overdue.Select(row => row.BookId));
        Assert.All(overdue, row => Assert.Equal(3, row.DaysLate));
        Assert.SequenceEqual(new[] { "Computing", "Fiction", "Science" }, Reports.Availability(tie).Select(row => row.Category));
        Assert.Equal(new CategoryRow("Computing", 2, 1, 0, 1), Reports.Availability(tie)[0]);
    }

    [TestCase]
    public void OverdueIteratorDefersWorkAndSnapshotCanBeReused()
    {
        var state = Sample();
        var enumerations = 0;
        IEnumerable<Loan> Source()
        {
            enumerations++;
            foreach (var loan in state.Loans)
                yield return loan;
        }
        var catalog = state.Books.ToDictionary(book => book.Id);
        var query = Reports.StreamOverdue(Source(), catalog, Today);
        Assert.Equal(0, enumerations);
        var snapshot = query.ToArray();
        Assert.Equal(1, enumerations);
        Assert.Equal("B01", Assert.Single(snapshot).BookId);
        Assert.Equal(3, snapshot.Sum(row => row.DaysLate));
        Assert.Equal(1, enumerations);
    }

    private static LibraryState Sample() => Value(LibraryState.Create(
        [
            Value(Book.Create("B01", "The Hobbit", "Fiction")),
            Value(Book.Create("B02", "The Little Prince", "Fiction")),
            Value(Book.Create("B03", "Functional Programming in C#", "Computing")),
            Value(Book.Create("B04", "Algorithms", "Computing")),
            Value(Book.Create("B05", "A Brief History of Time", "Science"))
        ],
        [
            Value(Member.Create("M01", "Dana Karim", MemberKind.Student, 0)),
            Value(Member.Create("M02", "Samat Lee", MemberKind.Staff, 0)),
            Value(Member.Create("M03", "Amina Nur", MemberKind.Student, 2_000))
        ],
        [
            Value(Loan.Create("B01", "M01", new(2026, 10, 26), new(2026, 11, 9), 0)),
            Value(Loan.Create("B03", "M02", new(2026, 10, 17), new(2026, 11, 14), 0))
        ],
        []));
    internal static T Value<T>(Result<T> result) => result switch
    {
        Result<T>.Success(var value) => value,
        Result<T>.Failure(var error) => throw new InvalidOperationException($"Unexpected failure [{error.Code}]: {error.Message}"),
        _ => throw new InvalidOperationException("Unknown result case.")
    };
    internal static void Failure<T>(Result<T> result, string code) =>
        Assert.Equal(code, Assert.IsType<Result<T>.Failure>(result).Error.Code);
}
