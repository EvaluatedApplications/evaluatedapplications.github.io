using System.Runtime.ExceptionServices;
using EvalApp;

namespace Doctrine;

/// <summary>
/// One way to turn a failed pipeline run into an exception. A <c>PipelineResult.Failure</c> can carry a null <c>Exception</c> (a step that
/// fails without throwing, or a race inside the runtime): <c>throw f.Exception</c> then raises a NullReferenceException that hides the
/// failure. Here a missing exception becomes an <see cref="InvalidOperationException"/> that says what was running (the context) and the
/// failure's own message; a real exception is rethrown with its own stack.
/// </summary>
internal static class PipelineFailure
{
    /// <summary>The exception to throw for <paramref name="f"/>; <paramref name="context"/> says what was running (round, seed, arena).</summary>
    internal static Exception ToException<T>(PipelineResult<T>.Failure f, string context)
        => f.Exception ?? new InvalidOperationException($"{context} failed with no exception: {(string.IsNullOrEmpty(f.Message) ? "(no message)" : f.Message)}");

    /// <summary>Throws when <paramref name="r"/> is a failure (a real exception keeps its stack); otherwise returns the run's data.</summary>
    internal static T DataOrThrow<T>(PipelineResult<T> r, string context)
    {
        if (r is PipelineResult<T>.Failure f)
        {
            var e = ToException(f, context);
            ExceptionDispatchInfo.Capture(e).Throw();
        }
        return r.GetData();
    }
}
