using Spectre.Console;
using Spectre.Console.Rendering;

namespace DotHelper.Ui;

/// <summary>
/// Deferred result output (user reports 1–3): while a <see cref="ScreenSession"/> owns the
/// alternate screen, result messages (success/error/outcome lines, tables, the transparency
/// commands) would disappear when the primary screen is restored. They are buffered here in
/// order and flushed after the session exits. Pickers and prompts never use this channel —
/// they are live interaction and write directly.
/// </summary>
public static class OutputChannel
{
    /// <summary>
    /// Writes immediately when no session is open; defers the write (in order) until the
    /// outermost session closes otherwise.
    /// </summary>
    public static void Write(IAnsiConsole console, Action<IAnsiConsole> write)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(write);

        if (ScreenSession.IsActive)
        {
            ScreenSession.Defer(console, write);
        }
        else
        {
            write(console);
        }
    }

    public static void Markup(IAnsiConsole console, string markup)
    {
        ArgumentNullException.ThrowIfNull(markup);
        Write(console, c => c.Markup(markup));
    }

    public static void MarkupLine(IAnsiConsole console, string markup)
    {
        ArgumentNullException.ThrowIfNull(markup);
        Write(console, c => c.MarkupLine(markup));
    }

    public static void WriteLine(IAnsiConsole console, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Write(console, c => c.WriteLine(text));
    }

    public static void WriteRenderable(IAnsiConsole console, IRenderable renderable)
    {
        ArgumentNullException.ThrowIfNull(renderable);
        Write(console, c => c.Write(renderable));
    }
}