namespace DotHelper.Ui;

/// <summary>
/// Esc in a prompt (user report 4): clean cancellation of the current flow. Unlike
/// <see cref="OperationCanceledException"/> (Ctrl+C → exit 130) this maps to the friendly
/// <c>Cancelled.</c> line and exit code 1 — the same contract as cancelling a picker inside a
/// flow. Never carries a stack trace to the user (see <c>Cli/CommandErrors</c>).
/// </summary>
public sealed class PromptCancelledException : Exception
{
    public PromptCancelledException()
        : base("The prompt was cancelled with Esc.")
    {
    }
}