using DotHelper.Ui;

using Spectre.Console;

namespace DotHelper.Cli;

/// <summary>
/// Uniform mapping from an unexpected exception to a friendly message and process exit code
/// (Fase 6 review M2): no stack traces, one <c>Error:</c>/<c>Cancelled.</c> line.
/// Registered once as the Spectre.Console.Cli exception handler. The lines go through
/// <see cref="OutputChannel"/> so they land on the primary screen after a fullscreen session
/// restores it.
/// </summary>
public static class CommandErrors
{
    /// <summary>Exit code for a cancelled run (Ctrl+C); everything else is a plain failure.</summary>
    public const int CancelledExitCode = 130;

    /// <summary>Exit code for every reported error.</summary>
    public const int ErrorExitCode = 1;

    /// <summary>Pure: maps an exception to the process exit code.</summary>
    /// <remarks>
    /// Ctrl+C (<see cref="OperationCanceledException"/>) is the only 130. Esc in a prompt
    /// (<see cref="PromptCancelledException"/>) is a clean user cancellation of the flow: the
    /// same <c>Cancelled.</c> line but exit 1, consistent with cancelling a picker.
    /// </remarks>
    public static int ExitCodeFor(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is OperationCanceledException ? CancelledExitCode : ErrorExitCode;
    }

    /// <summary>
    /// Prints the friendly line for <paramref name="exception"/> (no stack trace):
    /// <c>Cancelled.</c> for Ctrl+C and Esc-in-prompt cancellations, <c>Error: message</c>
    /// otherwise.
    /// </summary>
    public static void Print(Exception exception, IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(console);

        if (exception is OperationCanceledException or PromptCancelledException)
        {
            OutputChannel.WriteLine(console, "Cancelled.");
            return;
        }

        OutputChannel.MarkupLine(console, $"[red]Error:[/] {Markup.Escape(exception.Message)}");
    }

    /// <summary>Spectre.Console.Cli exception handler: print the line, return the exit code.</summary>
    public static int Handle(Exception exception)
    {
        Print(exception, AnsiConsole.Console);
        return ExitCodeFor(exception);
    }
}