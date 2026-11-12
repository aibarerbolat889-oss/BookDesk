using System.Text.Json;
using System.Text.Json.Serialization;
using static BookDesk.OutcomeExtensions;

namespace BookDesk;

public static class LibraryStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    public static Result<LibraryState> Load(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            var file = JsonSerializer.Deserialize<LibraryFile>(stream, Json);
            return file is null
                ? Fail<LibraryState>("invalid-data", "The JSON root must be a library object.")
                : ConvertFile(file);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return Fail<LibraryState>("file-missing", $"Library file was not found: {path}");
        }
        catch (JsonException exception)
        {
            return Fail<LibraryState>("malformed-json", $"Invalid JSON near {exception.Path ?? "$"}: {exception.Message}");
        }
        catch (Exception exception) when (IsFileError(exception))
        {
            return Fail<LibraryState>("file-read", exception.Message);
        }
    }

    public static Result<Change> Update(string path, Func<LibraryState, Result<Change>> operation)
    {
        FileStream updateLock;
        try
        {
            updateLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception exception) when (IsFileError(exception))
        {
            return Fail<Change>("update-lock", $"Cannot acquire the update lock: {exception.Message}");
        }

        using (updateLock)
            return Load(path).Bind(operation).Bind(change => Save(path, change));
    }

    private static Result<Change> Save(string path, Change change)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var ownsTemporaryFile = false;
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsTemporaryFile = true;
                JsonSerializer.Serialize(stream, ToFile(change.State), Json);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
            ownsTemporaryFile = false;
            return new Result<Change>.Success(change);
        }
        catch (Exception exception) when (IsFileError(exception))
        {
            return Fail<Change>("file-write", exception.Message);
        }
        finally
        {
            if (ownsTemporaryFile)
            {
                try { File.Delete(temp); }
                catch (Exception exception) when (IsFileError(exception)) { }
            }
        }
    }

    private static bool IsFileError(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private static Result<LibraryState> ConvertFile(LibraryFile file) =>
        ConvertRows(file.Books, "books", row => Book.Create(row.Id, row.Title, row.Category))
            .Bind(books => ConvertRows(file.Members, "members", ConvertMember)
                .Bind(members => ConvertRows(file.Loans, "loans",
                    row => Loan.Create(row.BookId, row.MemberId, row.Opened, row.Due, row.Renewals))
                    .Bind(loans => ConvertRows(file.Holds, "holds", row => Hold.Create(row.BookId, row.MemberId))
                        .Bind(holds => LibraryState.Create(books, members, loans, holds)))));

    private static Result<Member> ConvertMember(MemberRow row) => row.Kind switch
    {
        "student" => Member.Create(row.Id, row.Name, MemberKind.Student, row.Balance),
        "staff" => Member.Create(row.Id, row.Name, MemberKind.Staff, row.Balance),
        _ => Fail<Member>("invalid-member", "Member kind must be student or staff.")
    };

    private static Result<List<T>> ConvertRows<TRow, T>(TRow?[]? rows, string name, Func<TRow, Result<T>> convert)
        where TRow : class
    {
        if (rows is null)
            return Fail<List<T>>("invalid-data", $"The {name} collection is required.");
        var values = new List<T>(rows.Length);
        for (var index = 0; index < rows.Length; index++)
        {
            if (rows[index] is not { } row)
                return Fail<List<T>>("invalid-data", $"{name}[{index}] cannot be null.");
            switch (convert(row))
            {
                case Result<T>.Failure(var error):
                    return Fail<List<T>>(error.Code, $"{name}[{index}]: {error.Message}");
                case Result<T>.Success(var value):
                    values.Add(value);
                    break;
            }
        }
        return new Result<List<T>>.Success(values);
    }

    private static LibraryFile ToFile(LibraryState state) => new(
        state.Books.Select(book => new BookRow(book.Id, book.Title, book.Category)).ToArray(),
        state.Members.Select(member => new MemberRow(member.Id, member.Name,
            member.Kind == MemberKind.Student ? "student" : "staff", member.Balance)).ToArray(),
        state.Loans.Select(loan => new LoanRow(loan.BookId, loan.MemberId, loan.Opened, loan.Due, loan.Renewals)).ToArray(),
        state.Holds.Select(hold => new HoldRow(hold.BookId, hold.MemberId)).ToArray());

    private sealed record LibraryFile(
        [property: JsonRequired] BookRow?[]? Books,
        [property: JsonRequired] MemberRow?[]? Members,
        [property: JsonRequired] LoanRow?[]? Loans,
        [property: JsonRequired] HoldRow?[]? Holds);
    private sealed record BookRow(
        [property: JsonRequired] string? Id,
        [property: JsonRequired] string? Title,
        [property: JsonRequired] string? Category);
    private sealed record MemberRow(
        [property: JsonRequired] string? Id,
        [property: JsonRequired] string? Name,
        [property: JsonRequired] string? Kind,
        [property: JsonRequired] long Balance);
    private sealed record LoanRow(
        [property: JsonRequired] string? BookId,
        [property: JsonRequired] string? MemberId,
        [property: JsonRequired] DateOnly Opened,
        [property: JsonRequired] DateOnly Due,
        [property: JsonRequired] int Renewals);
    private sealed record HoldRow(
        [property: JsonRequired] string? BookId,
        [property: JsonRequired] string? MemberId);
}
