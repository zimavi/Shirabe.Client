using System.Diagnostics;

namespace Shirabe.Client.Services;

/// <summary>
/// Debug builds only: every exception that is thrown anywhere (also those the code catches and turns into a message
/// for the user, which is where most errors in the log come from) and every one that nobody catches is written to the
/// console. Calls to <see cref="Attach"/> are removed from Release builds by the compiler.
/// </summary>
public static class DebugLog
{
    [ThreadStatic]
    private static bool _writing;

    [Conditional("DEBUG")]
    public static void Attach()
    {
        AppDomain.CurrentDomain.FirstChanceException += (_, e) => Write("thrown", e.Exception, fullTrace: false);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("UNHANDLED", e.ExceptionObject as Exception, fullTrace: true);
        TaskScheduler.UnobservedTaskException += (_, e) => Write("unobserved in a task", e.Exception, fullTrace: true);
    }

    /// <summary>One block of text for the log: what happened, the exception's type and message, and where.</summary>
    /// <param name="fullTrace">All of the stack trace and inner exceptions; otherwise only the first few frames.</param>
    public static string Describe(string kind, Exception exception, bool fullTrace)
    {
        if (fullTrace) return $"[Shirabe] {kind}: {exception}";

        var frames = (exception.StackTrace ?? "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Take(4);
        return $"[Shirabe] {kind}: {exception.GetType().Name}: {exception.Message}{string.Concat(frames.Select(f => $"{Environment.NewLine}    {f}"))}";
    }

    private static void Write(string kind, Exception? exception, bool fullTrace)
    {
        // Cancelling is how work is stopped on purpose, not an error. Writing must never throw or recurse into itself.
        if (exception is null or OperationCanceledException || _writing) return;

        _writing = true;
        try
        {
            Console.Error.WriteLine(Describe(kind, exception, fullTrace));
        }
        catch (Exception)
        {
            // No console to write to: nothing else to do.
        }
        finally
        {
            _writing = false;
        }
    }
}
