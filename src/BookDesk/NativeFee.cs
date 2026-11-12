using System.Runtime.InteropServices;
using static BookDesk.OutcomeExtensions;

namespace BookDesk;

public static class NativeFee
{
    [DllImport("bookdesk_fee.dll", EntryPoint = "bookdesk_calculate_fee",
        CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    private static extern int CalculateFee(int daysLate, int rate, int grace, int cap, out int fee);

    public static Result<int> Calculate(FeeInput input)
    {
        try
        {
            var status = CalculateFee(input.DaysLate, input.RatePerDay, input.GraceDays, input.Cap, out var fee);
            return status == 0
                ? new Result<int>.Success(fee)
                : Fail<int>("native-rejected", $"Native fee calculator rejected the inputs (status {status}).");
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return Fail<int>("native-unavailable",
                $"Load failed ({exception.GetType().Name}). Use the Windows x64 library beside BookDesk.dll.");
        }
    }
}
