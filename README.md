# BookDesk

BookDesk is a Windows x64 console application for a small campus library. Desk staff can lend, renew and receive books, manage reservation queues, record fine payments, and check overdue loans and catalog availability.

## Project information

Introduction to Functional Programming final project, group IT-2514.

- Yerbolat Aibar
- Bektore Salybek
- Nurislam Ospanali

This implementation and its documentation were prepared with OpenAI Codex assistance. The course requires instructor permission and acknowledgment for AI use. Personal contributions and tests must be described by each student in their own words.

## Build and test

Use Windows x64 with the x64 .NET 8 SDK. The project was verified with SDK 8.0.131. The solution and its deterministic assertion runner use only the SDK and framework libraries.

Run from the project root in PowerShell.

```powershell
dotnet --version
dotnet restore BookDesk.sln
dotnet build BookDesk.sln -c Release --no-restore
dotnet run --project tests/BookDesk.Tests -c Release --no-build
dotnet run --project src/BookDesk -c Release --no-build -- --help
```

The prebuilt native DLL is in `runtimes/win-x64/native/bookdesk_fee.dll` and is copied beside the managed assembly. To rebuild it, use Visual Studio 2022 or Build Tools 2022 with Desktop development with C++, MSVC v143 and a Windows SDK. Open the **x64 Native Tools Command Prompt for VS 2022** in the project root and run the following command. Then rebuild the .NET solution to copy the new DLL to its output directory.

```bat
native\build-win-x64.cmd
```

The script compiles `native/bookdesk_fee.c` as C using MSVC and statically links the C runtime. The DLL exports `bookdesk_calculate_fee`; `NativeFee.Calculate` calls the Cdecl export with signed 32-bit integers and an out parameter. The C function uses a 64-bit intermediate and caps the result at 3,000 KZT. A missing, invalid or incompatible DLL fails the return operation before the JSON changes.

## Reproducible demo

Make a fresh copy of the sample each time you start the demo. The date is explicit so results can be reproduced on another day.

```powershell
New-Item -ItemType Directory -Force demo | Out-Null
Copy-Item data/library.sample.json demo/library.json -Force
$bookDesk = 'src/BookDesk/bin/Release/net8.0/BookDesk.dll'
dotnet $bookDesk demo/library.json overdue 2026-11-12
dotnet $bookDesk demo/library.json stock
dotnet $bookDesk demo/library.json checkout B02 M01 2026-11-12
dotnet $bookDesk demo/library.json renew B03 2026-11-12
dotnet $bookDesk demo/library.json hold B01 M02
dotnet $bookDesk demo/library.json return B01 2026-11-12
dotnet $bookDesk demo/library.json checkout B01 M03 2026-11-12
dotnet $bookDesk demo/library.json checkout B01 M02 2026-11-12
dotnet $bookDesk demo/library.json pay M01 300
```

The reports start with one overdue loan, B01 at three days late, and one available title in each category. B02 is due on 2026-11-26 after checkout. Renewing B03 extends its due date to 2026-11-21. M02 joins the B01 queue at position 1. Returning B01 applies 300 KZT to M01. M03 receives `hold-priority` and an exit code of 1. M02 can then check out B01 with a due date of 2026-12-10. The payment brings M01's balance to zero.

### Rules

| Workflow | Rule |
| --- | --- |
| Checkout | An available copy goes to the first waiting member, if any. Student loans last 14 days with up to 3 active loans. Staff loans last 28 days with up to 5 active loans. A balance of 2,000 KZT or more blocks checkout. |
| Renew | Extend an active loan by 7 days from its due date. The loan must be on time, never renewed, and free of waiting holds. |
| Hold | A member joins the tail of an unavailable book's queue. The active borrower and an already queued member are ineligible. |
| Return | Close the active loan and add the computed fee. The book remains reserved if the queue contains a waiting member. |
| Pay | Apply a positive payment in whole tenge, up to the current balance. |

The two fee policies charge 100 KZT per billable day up to 3,000 KZT. Staff get two grace days, and students pay from the first day. At two days late, the student fee is 200 KZT and the staff fee is 0 KZT. The overdue report counts calendar days since the due date for all members. A book due today is on time.

## Source map

| File | Responsibility |
| --- | --- |
| `Models.cs` | Validated factories, immutable models and complete state validation |
| `Outcomes.cs` | `Result`, `Option`, and custom extensions `Map`, `Bind`, `Find`, `Require` |
| `Library.cs` | Five workflows, checkout pipeline, curried terms and injected fee policies |
| `Reports.cs` | Deferred overdue iterator and two materialized LINQ reports |
| `LibraryStore.cs` | JSON conversion, validation, writer lock and atomic file replacement |
| `NativeFee.cs` and `native/` | Managed adapter, native implementation and Windows x64 build script |
| `Program.cs` | CLI parsing, calling workflows and printing outcomes |
| `tests/BookDesk.Tests/` | Assertions for domain rules, complete workflows, reports, persistence and native interop |

Checkout resolves references, checks eligibility, creates a validated loan, and registers a new state. The return workflow requires an active loan and a valid operation date before invoking the fee delegate. Result failures stop downstream operations. The reports work on immutable loaded data. `Overdue` calls `ToArray` after sorting so the result can be enumerated repeatedly without re-running the iterator.

The JSON has four required arrays `books`, `members`, `loans` and `holds`. Hold array order records queue priority. Loaded rows go through the same validated factories as command inputs. A modifying command acquires a writer lock on the full path, loads and validates the latest state, and saves only on success. The store closes the output stream before replacing the original with the flushed temporary file in the same directory. The lock file can remain on disk after release. Readers access a complete snapshot. Failed operations leave the input data intact. A completed loan is removed and its fee balance is retained. For the demo, work on a copy of the synthetic sample.

## Limitations

This version supports one physical copy per book ID, local single-file storage and a Windows x64 native binary. It keeps active loans and current balances without maintaining a loan or payment history. The queue has no expiry and there is no automatic fine collection. Dates range from 2000 to 2100. The application assumes the JSON is managed through the CLI during concurrent use.

## Version

Branch `main`. The Git tag `final-exam` identifies the release commit. From a clone, run `git rev-parse final-exam^{commit}` in Git Bash or `git rev-parse 'final-exam^{commit}'` in PowerShell. The release metadata accompanying the archive records the full commit ID. The hosted URL is pending.

To publish the exact snapshot, clone the provided Git bundle with `git clone -b main BookDesk.git.bundle BookDesk`. Create an empty hosted repository for the team, set the clone's origin to its URL with `git remote set-url origin <repository-url>`, then run `git push -u origin main` and `git push origin final-exam`. If the repository is private, grant the instructor access. Add the hosted URL to the report and release metadata.

## References

- [Microsoft Learn on platform invocation](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/pinvoke)
- [Microsoft Learn on unmapped JSON members](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/missing-members)
- Course Final Project Requirements and Moodle Final Exam brief
