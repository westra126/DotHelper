using DotHelper.Ui;

using FluentAssertions;

using Spectre.Console;
using Spectre.Console.Testing;

namespace DotHelper.Tests.Unit;

/// <summary>
/// <see cref="ScreenSession"/> (user reports 1–3): one alternate-screen entry per invocation,
/// reentrant scopes, capability no-op (zero escapes without a real interactive terminal) and the
/// deferred result buffer flushed in order after the exit sequence.
///
/// The session is process-static (depth, entered flag, pending buffer), so every test class that
/// touches it joins this xUnit collection and runs serially against the others.
/// </summary>
[Collection("ScreenSession")]
public sealed class ScreenSessionTests
{
    [Theory]
    [InlineData(false, false, true, true, true, true, "real interactive terminal, real keys")]
    [InlineData(false, true, true, true, true, false, "an injected key reader never touches the terminal buffer")]
    [InlineData(false, false, false, true, true, false, "no ANSI: no escape sequences at all")]
    [InlineData(false, false, true, false, true, false, "no alternate buffer support")]
    [InlineData(false, false, true, true, false, false, "TestConsole and other non-interactive consoles")]
    [InlineData(true, false, true, true, true, false, "redirected stdin (pipes, --json runs) is a total no-op")]
    public void ShouldEnter_requires_a_real_interactive_terminal(
        bool inputRedirected, bool hasInjectedKeyReader, bool ansi, bool alternateBuffer, bool interactive,
        bool expected, string because)
    {
        ScreenGate.ShouldEnter(inputRedirected, hasInjectedKeyReader, ansi, alternateBuffer, interactive)
            .Should().Be(expected, because);
    }

    [Fact]
    public void Without_capabilities_the_session_is_a_total_no_op()
    {
        // TestConsole reports Interactive = false: nothing is entered, nothing is buffered.
        var console = NewAnsiConsole();

        ScreenSession.Run(console, () => OutputChannel.WriteLine(console, "result line"), ScreenGate.For(console));

        console.Output.Should().NotContain("\u001b[?1049h");
        console.Output.Should().NotContain("\u001b[?1049l");
        console.Output.Should().Contain("result line", "without a session the write goes through immediately");
    }

    [Fact]
    public void A_capable_session_enters_once_and_leaves_once()
    {
        // The screen opens lazily on the first interactive frame (picker/prompt EnsureOpen) so
        // non-interactive runs emit nothing at all.
        var console = NewAnsiConsole();

        ScreenSession.Run(
            console,
            () => ScreenSession.EnsureOpen(console, hasInjectedKeyReader: false),
            ScreenGate.Capable);

        EnterCount(console).Should().Be(1);
        LeaveCount(console).Should().Be(1);
    }

    [Fact]
    public void Nested_sessions_keep_a_single_enter_exit_pair()
    {
        // The wizard wraps menu + sub-menus + the dispatched flow: reentrant scopes are no-ops.
        var console = NewAnsiConsole();

        ScreenSession.Run(
            console,
            () =>
            {
                ScreenSession.EnsureOpen(console, hasInjectedKeyReader: false);
                ScreenSession.Run(
                    console,
                    () => ScreenSession.EnsureOpen(console, hasInjectedKeyReader: false),
                    ScreenGate.Capable);
            },
            ScreenGate.Capable);

        EnterCount(console).Should().Be(1, "the inner scopes must not touch the terminal buffer again");
        LeaveCount(console).Should().Be(1);
    }

    [Fact]
    public void Result_messages_are_deferred_in_order_until_the_session_closes()
    {
        var console = NewAnsiConsole();

        ScreenSession.Run(
            console,
            () =>
            {
                ScreenSession.EnsureOpen(console, hasInjectedKeyReader: false);
                OutputChannel.MarkupLine(console, "first");
                OutputChannel.WriteLine(console, "second");
                OutputChannel.MarkupLine(console, "third");
                console.WriteLine("interaction writes go through directly");
            },
            ScreenGate.Capable);

        string output = console.Output;
        int leave = output.IndexOf("\u001b[?1049l", StringComparison.Ordinal);
        leave.Should().BeGreaterThan(0, "the session must restore the primary screen");
        output.IndexOf("first", StringComparison.Ordinal).Should().BeGreaterThan(leave, "results land after the restore");
        output.IndexOf("second", StringComparison.Ordinal).Should().BeGreaterThan(output.IndexOf("first", StringComparison.Ordinal));
        output.IndexOf("third", StringComparison.Ordinal).Should().BeGreaterThan(output.IndexOf("second", StringComparison.Ordinal));
        output.IndexOf("interaction writes go through directly", StringComparison.Ordinal)
            .Should().BeLessThan(leave, "interaction is never buffered");
    }

    [Fact]
    public void An_exception_still_restores_the_screen_and_flushes_the_results()
    {
        var console = NewAnsiConsole();

        Action act = () => ScreenSession.Run<int>(
            console,
            () =>
            {
                ScreenSession.EnsureOpen(console, hasInjectedKeyReader: false);
                OutputChannel.WriteLine(console, "before the failure");
                throw new InvalidOperationException("boom");
            },
            ScreenGate.Capable);

        act.Should().Throw<InvalidOperationException>();
        EnterCount(console).Should().Be(1);
        LeaveCount(console).Should().Be(1, "Ctrl+C and crashes must restore the screen");
        console.Output.Should().Contain("before the failure");
    }

    [Fact]
    public void EnsureOpen_is_a_no_op_outside_a_scope_and_with_injected_keys()
    {
        var console = NewAnsiConsole();

        ScreenSession.EnsureOpen(console, hasInjectedKeyReader: false);
        ScreenSession.EnsureOpen(console, hasInjectedKeyReader: true);

        EnterCount(console).Should().Be(0);
    }

    [Fact]
    public void Writes_outside_a_session_are_immediate()
    {
        var console = new TestConsole();

        OutputChannel.MarkupLine(console, "instant");

        console.Output.Should().Contain("instant");
        ScreenSession.IsActive.Should().BeFalse();
    }

    /// <summary>TestConsole that actually emits the escape sequences so they can be counted.</summary>
    private static TestConsole NewAnsiConsole()
    {
        var console = new TestConsole();
        console.EmitAnsiSequences();
        return console;
    }

    private static int EnterCount(TestConsole console) =>
        Count(console.Output, "\u001b[?1049h");

    private static int LeaveCount(TestConsole console) =>
        Count(console.Output, "\u001b[?1049l");

    private static int Count(string haystack, string needle)
    {
        int count = 0;
        for (int index = haystack.IndexOf(needle, StringComparison.Ordinal);
             index >= 0;
             index = haystack.IndexOf(needle, index + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}

/// <summary>Serializes every test class that touches the process-static <see cref="ScreenSession"/> state.</summary>
[CollectionDefinition("ScreenSession")]
public sealed class ScreenSessionCollection
{
}