namespace DotHelper.Ui;

/// <summary>
/// Esc in a dialog (user report 4, generalized by the back-navigation report): the ONE
/// "user backed out" signal of the application — <see cref="FuzzyPicker{T}"/> and
/// <c>Prompts</c> both raise it on Esc. Unlike <see cref="OperationCanceledException"/>
/// (Ctrl+C → exit 130) it never carries a stack trace to the user (see <c>Cli/CommandErrors</c>).
///
/// Inside a flow's step machine (<see cref="FlowNavigator"/>) it is caught and translated into
/// "go back one step"; when it escapes a flow it means Esc at the flow's first interactive
/// step, and each caller maps it explicitly: a direct command reports <c>Cancelled.</c> and
/// exits 1, the root wizard returns to the menu it dispatched from.
/// </summary>
public sealed class PromptCancelledException : Exception
{
    public PromptCancelledException()
        : base("The prompt was cancelled with Esc.")
    {
    }
}