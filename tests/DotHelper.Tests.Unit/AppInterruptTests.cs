using DotHelper.Core;

using FluentAssertions;

namespace DotHelper.Tests.Unit;

/// <summary>
/// <see cref="AppInterrupt"/> (Ctrl+C report): the global CancelKeyPress wiring that turns
/// Ctrl+C into a cancellation (exit 130) instead of a process kill with a stack trace. The
/// token is linked into <c>DotnetRunner</c>, so a SIGINT during a <c>dotnet</c> run cancels the
/// wait and kills the process tree. The <c>e.Cancel = true</c> event glue cannot be unit-tested
/// (<see cref="ConsoleCancelEventArgs"/> has no public constructor) and is covered by the PTY
/// evidence; everything else is exercised here. The process-static state is reset after every
/// test and this class joins the <c>ProcessState</c> collection so real runner invocations can
/// never observe a cancelled global token.
/// </summary>
[Collection("ProcessState")]
public sealed class AppInterruptTests : IDisposable
{
    public void Dispose()
    {
        AppInterrupt.ResetForTests();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Cancel_cancels_the_global_token()
    {
        AppInterrupt.ResetForTests();

        AppInterrupt.Cancel();

        AppInterrupt.Token.IsCancellationRequested.Should().BeTrue(
            "Ctrl+C must cancel the run, not kill the process");
    }

    [Fact]
    public void Cancel_is_safe_to_invoke_twice()
    {
        AppInterrupt.ResetForTests();

        Action act = () =>
        {
            AppInterrupt.Cancel();
            AppInterrupt.Cancel();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void Link_observes_the_global_interrupt()
    {
        // The path a `dotnet` run takes: its wait token is linked to the global interrupt.
        AppInterrupt.ResetForTests();
        using CancellationTokenSource linked = AppInterrupt.Link(CancellationToken.None);

        AppInterrupt.Cancel();

        linked.Token.IsCancellationRequested.Should().BeTrue(
            "the runner's wait must observe Ctrl+C and kill the process tree");
    }

    [Fact]
    public void Link_observes_the_callers_token_too()
    {
        AppInterrupt.ResetForTests();
        using CancellationTokenSource external = new();
        using CancellationTokenSource linked = AppInterrupt.Link(external.Token);

        external.Cancel();

        linked.Token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void Wire_is_idempotent()
    {
        AppInterrupt.ResetForTests();

        Action act = () =>
        {
            AppInterrupt.Wire();
            AppInterrupt.Wire();
        };

        act.Should().NotThrow("wiring must be safe to call from every entry point");
    }

    [Fact]
    public void ResetForTests_restores_a_fresh_token()
    {
        AppInterrupt.ResetForTests();
        AppInterrupt.Cancel();

        AppInterrupt.ResetForTests();

        AppInterrupt.Token.IsCancellationRequested.Should().BeFalse();
    }
}

/// <summary>
/// Serializes every test class that touches the process-static interrupt state or starts real
/// <c>dotnet</c> processes through the runner (which observes that state).
/// </summary>
[CollectionDefinition("ProcessState")]
public sealed class ProcessStateCollection
{
}