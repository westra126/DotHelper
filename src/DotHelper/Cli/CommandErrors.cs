using Spectre.Console;

namespace DotHelper.Cli;

/// <summary>
/// Uniform mapping from an unexpected exception to a friendly message and process exit code
/// (Fase 6 review M2): no stack traces, one <c>Error:</c>/<c>Cancelled.</c> line.
/// Registered once as the Spectre.Console.Cli exception handler.
/// </summary>
public static class CommandErrors
{
    /// <summary>Exit code for a cancelled run (Ctrl+C); everything else is a plain failure.</summary>
    public const int CancelledExitCode = 130;

    /// <summary>Exit code for every reported error.</summary>
    public const int ErrorExitCode = 1;

    /// <summary>Pure: maps an exception to the process exit code.</summary>
    public static int ExitCodeFor(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is OperationCanceledException ? CancelledExitCode : ErrorExitCode;
    }

    /// <summary>
    /// Prints the friendly line for <paramref name="exception"/> (no stack trace):
    /// <c>Cancelled.</c> for cancellations, <c>Error: message</c> otherwise.
    /// </summary>
    public static void Print(Exception exception, IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentNullException.ThrowIfNull(console);

        if (exception is OperationCanceledException)
        {
            console.WriteLine("Cancelled.");
            return;
        }

        console.MarkupLine($"[red]Error:[/] {Markup.Escape(exception.Message)}");
    }

    /// <summary>Spectre.Console.Cli exception handler: print the line, return the exit code.</summary>
    public static int Handle(Exception exception)
    {
        Print(exception, AnsiConsole.Console);
        return ExitCodeFor(exception);
    }
}