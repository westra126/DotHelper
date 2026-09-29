namespace DotHelper.Core;

/// <summary>
/// Process-wide Ctrl+C interrupt (user report: interrupt at any moment, exit 130).
///
/// Wiring: <c>Program</c> calls <see cref="Wire"/> once, which hooks
/// <see cref="Console.CancelKeyPress"/> and suppresses the default process kill
/// (<c>e.Cancel = true</c>) in favour of cancelling <see cref="Token"/> via <see cref="Cancel"/>.
/// That token is accessible from every flow and is linked into <c>DotnetRunner</c>, so a SIGINT
/// during a <c>dotnet</c> run cancels the wait and kills the whole process tree; the
/// cancellation surfaces as <see cref="OperationCanceledException"/>, which the global handler
/// maps to <c>Cancelled.</c> after the alternate screen is restored and exit code 130.
///
/// Inside pickers/prompts <c>Console.TreatControlCAsInput</c> is on, so Ctrl+C arrives as a
/// key and follows the same 130 contract without touching this type — the two paths are
/// complementary, never in conflict.
/// </summary>
public static class AppInterrupt
{
    private static CancellationTokenSource _cts = new();
    private static bool _wired;

    /// <summary>The global cancellation token, cancelled by Ctrl+C (or <see cref="Handle"/>).</summary>
    public static CancellationToken Token => _cts.Token;

    /// <summary>
    /// Hooks <see cref="Console.CancelKeyPress"/> exactly once (idempotent): Ctrl+C cancels
    /// <see cref="Token"/> instead of terminating the process with a stack trace.
    /// </summary>
    public static void Wire()
    {
        if (_wired)
        {
            return;
        }

        _wired = true;
        Console.CancelKeyPress += static (_, e) =>
        {
            // Suppress the default kill: the run unwinds cleanly (screen restored, exit 130).
            e.Cancel = true;
            Cancel();
        };
    }

    /// <summary>Links a caller's token with the global interrupt (used by the process runner).</summary>
    public static CancellationTokenSource Link(CancellationToken external) =>
        CancellationTokenSource.CreateLinkedTokenSource(external, Token);

    /// <summary>
    /// The Ctrl+C action: cancels the global token. Internal seam for tests — the event glue
    /// (<c>e.Cancel = true</c> + this call) cannot be unit-tested because
    /// <see cref="ConsoleCancelEventArgs"/> has no public constructor; the PTY evidence covers
    /// the real signal path.
    /// </summary>
    internal static void Cancel()
    {
        if (!_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }
    }

    /// <summary>Test seam: a fresh (uncancelled) token state.</summary>
    internal static void ResetForTests() => _cts = new CancellationTokenSource();
}