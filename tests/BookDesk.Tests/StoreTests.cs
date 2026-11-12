using static BookDesk.Tests.LibraryTests;

namespace BookDesk.Tests;

public sealed class StoreTests : IDisposable
{
    private readonly string directory = Directory.CreateDirectory(
        Path.Combine(AppContext.BaseDirectory, $"bookdesk-tests-{Guid.NewGuid():N}")).FullName;
    private string LibraryPath => Path.Combine(directory, "library.json");

    [TestCase]
    public void MissingFileReturnsExplicitError()
    {
        Failure(LibraryStore.Load(LibraryPath), "file-missing");
        Assert.False(File.Exists(LibraryPath));
    }

    [TestCase]
    public void MalformedJsonReturnsErrorAndReleasesReadHandle()
    {
        File.WriteAllText(LibraryPath, "{ \"books\": [");
        Failure(LibraryStore.Load(LibraryPath), "malformed-json");
        using var exclusive = File.Open(LibraryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(exclusive.CanWrite);
    }

    [TestCase]
    public void ValidJsonWithDanglingHoldIsRejected()
    {
        File.WriteAllText(LibraryPath, """
            {"books":[],"members":[],"loans":[],"holds":[{"bookId":"B01","memberId":"M01"}]}
            """);
        Failure(LibraryStore.Load(LibraryPath), "invalid-state");
    }

    [TestCase]
    public void RequiredJsonPropertyCannotDefaultSilently()
    {
        File.WriteAllText(LibraryPath, """
            {"books":[],"members":[{"id":"M01","name":"Dana","kind":"student"}],"loans":[],"holds":[]}
            """);
        Failure(LibraryStore.Load(LibraryPath), "malformed-json");
    }

    [TestCase]
    public void FailedUpdateKeepsFileBytesAndReleasesLockForSuccessfulRetry()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "library.sample.json"), LibraryPath);
        var before = File.ReadAllBytes(LibraryPath);
        var today = new DateOnly(2026, 11, 12);
        Failure(LibraryStore.Update(LibraryPath, state => Library.Return(state, "B01", today,
            _ => OutcomeExtensions.Fail<int>("native-unavailable", "Simulated loader failure."))), "native-unavailable");
        Assert.SequenceEqual(before, File.ReadAllBytes(LibraryPath));
        Value(LibraryStore.Update(LibraryPath, state => Library.Return(state, "B01", today, NativeFee.Calculate)));
        var reloaded = Value(LibraryStore.Load(LibraryPath));
        Assert.DoesNotContain(reloaded.Loans, loan => loan.BookId == "B01");
        Assert.Equal(300, reloaded.Members[0].Balance);
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
    }

    [TestCase]
    public void UpdateLockRejectsConcurrentWriterBeforeOperation()
    {
        using var activeWriter = File.Open(LibraryPath + ".lock", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        var calls = 0;
        Failure(LibraryStore.Update(LibraryPath, _ =>
        {
            calls++;
            return OutcomeExtensions.Fail<Change>("unexpected", "Operation should not be reached.");
        }), "update-lock");
        Assert.Equal(0, calls);
    }

    public void Dispose()
    {
        foreach (var file in Directory.EnumerateFiles(directory))
            File.Delete(file);
        Directory.Delete(directory);
    }
}
