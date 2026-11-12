using System.Reflection;
using BookDesk.Tests;

Type[] suites = [typeof(LibraryTests), typeof(StoreTests)];
var passed = 0;
var failed = 0;
foreach (var suite in suites)
{
    var cases = suite.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
        .Where(method => method.IsDefined(typeof(TestCaseAttribute)))
        .OrderBy(method => method.Name, StringComparer.Ordinal);
    foreach (var test in cases)
    {
        var name = $"{suite.Name}.{test.Name}";
        try
        {
            var instance = Activator.CreateInstance(suite);
            using var resources = instance as IDisposable;
            test.Invoke(instance, null);
        }
        catch (Exception exception)
        {
            var cause = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
            Console.Error.WriteLine($"FAIL {name}: {cause.Message}");
            failed++;
            continue;
        }
        Console.WriteLine($"PASS {name}");
        passed++;
    }
}

Console.WriteLine($"Total: {passed + failed}, passed: {passed}, failed: {failed}");
return failed == 0 && passed > 0 ? 0 : 1;
