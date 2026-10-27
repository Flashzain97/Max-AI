namespace Max;

internal static class Execution
{
    [ThreadStatic] private static bool dedicatedStack;
    internal static bool HasDedicatedStack => dedicatedStack;

    internal static T Run<T>(Func<T> action)
    {
        if (dedicatedStack) return action();
        // Interpreter errors can pass through many nested CLR handlers. A
        // consistent stack keeps the language limits safe in small-stack hosts.
        T result = default!;
        Exception? failure = null;
        var worker = new System.Threading.Thread(() =>
        {
            dedicatedStack = true;
            try { result = action(); }
            catch (Exception error) { failure = error; }
        }, 16 * 1024 * 1024);
        worker.Start();
        worker.Join();
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }

    internal static void Run(Action action) => Run(() => { action(); return true; });
}
