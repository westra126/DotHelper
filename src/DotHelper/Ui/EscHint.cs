namespace DotHelper.Ui;

/// <summary>
/// Contextual meaning of Esc in a dialog (user report: Esc now navigates back). The value is
/// rendered as the dialog's hint line — literally <c>Esc back</c>, <c>Esc exit</c> or
/// <c>Esc cancel</c> — so the user always knows what Esc will do before pressing it:
/// <list type="bullet">
/// <item><see cref="Exit"/> — root wizard menu: Esc leaves the application (exit 0).</item>
/// <item><see cref="Back"/> — sub-menus and intermediate flow steps: Esc goes back one level.</item>
/// <item><see cref="Cancel"/> — first interactive step of a direct command: Esc aborts the
/// flow (<c>Cancelled.</c>, exit 1).</item>
/// </list>
/// </summary>
public enum EscHint
{
    /// <summary>Esc cancels the whole flow (<c>Cancelled.</c>, exit 1).</summary>
    Cancel,

    /// <summary>Esc goes back one level (previous step / parent menu).</summary>
    Back,

    /// <summary>Esc exits the application (exit 0).</summary>
    Exit,
}

/// <summary>Rendering of the contextual <see cref="EscHint"/> labels.</summary>
public static class EscHintText
{
    /// <summary>The literal hint shown in a dialog: <c>Esc back</c>, <c>Esc exit</c>, <c>Esc cancel</c>.</summary>
    public static string For(EscHint hint) =>
        hint switch
        {
            EscHint.Back => "Esc back",
            EscHint.Exit => "Esc exit",
            _ => "Esc cancel",
        };
}