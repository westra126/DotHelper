namespace DotHelper.Ui;

/// <summary>
/// Pure step machine of an interactive flow (user report: Esc rewinds step by step). The flow
/// runs its steps in order; a step that actually asks the user (picker or prompt) is
/// <em>visible</em> and becomes a rewind target, while auto-resolved steps (provided flags,
/// single candidate, <c>--yes</c>) are transparent: they never become rewind targets and never
/// swallow Esc.
///
/// Semantics (documented contract):
/// <list type="number">
/// <item>Esc inside a step's dialog rewinds to the previous <em>visible</em> step
/// (<see cref="TryRewind"/> returns <c>true</c>); every step in between re-runs, and the
/// answers already given are kept by the flow and offered again as defaults/preselections.</item>
/// <item>Esc at the flow's first visible step does not rewind (<see cref="TryRewind"/> returns
/// <c>false</c>): the flow exits — the root wizard returns to the menu it dispatched from, a
/// direct command reports <c>Cancelled.</c> and exits 1 (via
/// <see cref="PromptCancelledException"/>).</item>
/// </list>
/// </summary>
public sealed class FlowNavigator
{
    private readonly List<int> _visible = [];
    private readonly EscHint _firstStepEsc;
    private int _step;

    /// <param name="firstStepEsc">
    /// Esc hint (and behaviour hint) of the flow's first interactive step: <see cref="EscHint.Cancel"/>
    /// for direct commands, <see cref="EscHint.Back"/> for wizard-dispatched flows (Esc returns
    /// to the menu).
    /// </param>
    public FlowNavigator(EscHint firstStepEsc = EscHint.Cancel) => _firstStepEsc = firstStepEsc;

    /// <summary>Index of the step being executed.</summary>
    public int Step => _step;

    /// <summary>Completes the current step and moves to the next one.</summary>
    public void Next() => _step++;

    /// <summary>
    /// Records that the current step is about to ask the user and returns the Esc hint for that
    /// dialog: the configured first-step hint while the flow is still on its first visible step,
    /// <see cref="EscHint.Back"/> afterwards. Must be called immediately before showing the
    /// picker/prompt — never for auto-resolved steps.
    /// </summary>
    public EscHint Ask()
    {
        if (_visible.Count == 0 || _visible[^1] != _step)
        {
            _visible.Add(_step);
        }

        return _visible.Count == 1 ? _firstStepEsc : EscHint.Back;
    }

    /// <summary>
    /// Esc was pressed inside the current step's dialog: rewinds to the previous visible step
    /// and returns <c>true</c>, or returns <c>false</c> when the flow's first visible step was
    /// the one asking (the caller must exit the flow by letting
    /// <see cref="PromptCancelledException"/> propagate).
    /// </summary>
    public bool TryRewind()
    {
        if (_visible.Count <= 1)
        {
            return false;
        }

        _visible.RemoveAt(_visible.Count - 1);
        _step = _visible[^1];
        return true;
    }
}