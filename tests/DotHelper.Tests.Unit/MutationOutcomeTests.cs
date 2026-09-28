using DotHelper.Cli;
using DotHelper.Core.Dotnet;

using FluentAssertions;

using Spectre.Console;
using Spectre.Console.Testing;

namespace DotHelper.Tests.Unit;

/// <summary>
/// M1 review: mutating flows must report <c>dotnet</c> failures as exit 1 with a friendly
/// <c>Error:</c> line — never a green "✔ Created" with exit 0. The decision logic lives in
/// <see cref="CliSupport.IsFailed"/> / <see cref="CliSupport.FinishMutation"/>; output is
/// captured by swapping <see cref="AnsiConsole.Console"/> for a <see cref="TestConsole"/>.
/// </summary>
public sealed class MutationOutcomeTests
{
    [Fact]
    public void IsFailed_is_true_only_for_real_non_zero_runs()
    {
        CliSupport.IsFailed(Result(exitCode: 1)).Should().BeTrue();
        CliSupport.IsFailed(Result(exitCode: 73)).Should().BeTrue("dotnet new exits 73 on existing files");
        CliSupport.IsFailed(Result(exitCode: 0)).Should().BeFalse();
        CliSupport.IsFailed(Result(exitCode: 1, dryRun: true)).Should().BeFalse("a dry-run never executed");
    }

    [Fact]
    public void FinishMutation_reports_failure_as_exit_1_without_the_success_mark()
    {
        TestConsole console = Capture(out IAnsiConsole previous);
        try
        {
            int exit = CliSupport.FinishMutation(
                new WorkspaceCommandSettings(),
                Result(exitCode: 73, stdErr: "error: Creating this template will make changes to existing files."),
                "Created P/P.csproj",
                "would create P/P.csproj");

            exit.Should().Be(1);
            console.Output.Should().Contain("Error: Creating this template will make changes");
            console.Output.Should().NotContain("✔", "a failed run must not claim success");
        }
        finally
        {
            AnsiConsole.Console = previous;
        }
    }

    [Fact]
    public void FinishMutation_prints_success_and_returns_0_on_success()
    {
        TestConsole console = Capture(out IAnsiConsole previous);
        try
        {
            int exit = CliSupport.FinishMutation(
                new WorkspaceCommandSettings(),
                Result(exitCode: 0),
                "Created P/P.csproj",
                "would create P/P.csproj");

            exit.Should().Be(0);
            console.Output.Should().Contain("✔ Created P/P.csproj");
            console.Output.Should().Contain("dotnet new classlib");
        }
        finally
        {
            AnsiConsole.Console = previous;
        }
    }

    [Fact]
    public void FinishMutation_keeps_the_unmistakable_dry_run_message()
    {
        TestConsole console = Capture(out IAnsiConsole previous);
        try
        {
            int exit = CliSupport.FinishMutation(
                new WorkspaceCommandSettings(),
                Result(exitCode: 0, dryRun: true),
                "Created P/P.csproj",
                "would create P/P.csproj");

            exit.Should().Be(0);
            console.Output.Should().Contain("Dry-run: would create P/P.csproj");
            console.Output.Should().NotContain("✔");
        }
        finally
        {
            AnsiConsole.Console = previous;
        }
    }

    [Fact]
    public void RejectFlagLike_blocks_values_that_look_like_flags()
    {
        CliSupport.LooksLikeFlag("--force").Should().BeTrue();
        CliSupport.LooksLikeFlag("-x").Should().BeTrue();
        CliSupport.LooksLikeFlag("App").Should().BeFalse();
        CliSupport.LooksLikeFlag("").Should().BeFalse();
        CliSupport.LooksLikeFlag(null).Should().BeFalse();

        TestConsole console = Capture(out IAnsiConsole previous);
        try
        {
            CliSupport.RejectFlagLike("--force", "Project name").Should().BeTrue();
            console.Output.Should().Contain("Project name must not start with '-': --force");
            CliSupport.RejectFlagLike("App", "Project name").Should().BeFalse();
        }
        finally
        {
            AnsiConsole.Console = previous;
        }
    }

    private static TestConsole Capture(out IAnsiConsole previous)
    {
        previous = AnsiConsole.Console;
        var console = new TestConsole();
        AnsiConsole.Console = console;
        return console;
    }

    private static DotnetResult Result(int exitCode, bool dryRun = false, string stdErr = "") =>
        new()
        {
            ExitCode = exitCode,
            StdOut = string.Empty,
            StdErr = stdErr,
            CommandLine = "dotnet new classlib -n P -o P",
            DryRun = dryRun,
        };
}