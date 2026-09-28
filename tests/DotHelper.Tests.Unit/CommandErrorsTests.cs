using DotHelper.Cli;
using DotHelper.Ui;

using FluentAssertions;

using Spectre.Console;
using Spectre.Console.Testing;

namespace DotHelper.Tests.Unit;

/// <summary>
/// M2 review: one global exception handler maps every failure to a friendly line and a stable
/// exit code — Ctrl+C 130, Esc-in-prompt and everything else 1, never a stack trace. Touches the
/// process-static <see cref="ScreenSession"/> output channel, so it joins the serializing
/// collection.
/// </summary>
[Collection("ScreenSession")]
public sealed class CommandErrorsTests
{
    [Theory]
    [InlineData(typeof(OperationCanceledException), 130)]
    [InlineData(typeof(TaskCanceledException), 130)]
    [InlineData(typeof(PromptCancelledException), 1)]
    [InlineData(typeof(InvalidOperationException), 1)]
    [InlineData(typeof(ArgumentException), 1)]
    [InlineData(typeof(Exception), 1)]
    public void ExitCodeFor_maps_exceptions_to_exit_codes(Type exceptionType, int expected)
    {
        Exception exception = (Exception)Activator.CreateInstance(exceptionType)!;

        CommandErrors.ExitCodeFor(exception).Should().Be(expected);
    }

    [Fact]
    public void Cancellations_print_cancelled_and_exit_130()
    {
        IAnsiConsole previous = AnsiConsole.Console;
        var console = new TestConsole();
        AnsiConsole.Console = console;
        try
        {
            int exit = CommandErrors.Handle(new OperationCanceledException());

            exit.Should().Be(130);
            console.Output.Should().Contain("Cancelled.");
        }
        finally
        {
            AnsiConsole.Console = previous;
        }
    }

    [Fact]
    public void Errors_print_the_message_only_and_exit_1()
    {
        IAnsiConsole previous = AnsiConsole.Console;
        var console = new TestConsole();
        AnsiConsole.Console = console;
        try
        {
            int exit = CommandErrors.Handle(new InvalidOperationException("Unknown solution format 'wtf'. Use 'sln' or 'slnx'."));

            exit.Should().Be(1);
            console.Output.Should().Contain("Error: Unknown solution format 'wtf'. Use 'sln' or 'slnx'.");
            console.Output.Should().NotContain("   at ", "no stack traces");
        }
        finally
        {
            AnsiConsole.Console = previous;
        }
    }

    [Fact]
    public void Esc_in_a_prompt_prints_cancelled_and_exits_1()
    {
        // User report 4: cancelling a text dialog is a clean flow cancellation — same friendly
        // line as Ctrl+C but the picker-consistent exit code 1, never a stack trace.
        IAnsiConsole previous = AnsiConsole.Console;
        var console = new TestConsole();
        AnsiConsole.Console = console;
        try
        {
            int exit = CommandErrors.Handle(new PromptCancelledException());

            exit.Should().Be(1);
            console.Output.Should().Contain("Cancelled.");
            console.Output.Should().NotContain("   at ", "no stack traces");
        }
        finally
        {
            AnsiConsole.Console = previous;
        }
    }

    [Fact]
    public void Print_accepts_an_injected_console()
    {
        var console = new TestConsole();

        CommandErrors.Print(new InvalidOperationException("boom"), console);

        console.Output.Should().Contain("Error: boom");
    }
}