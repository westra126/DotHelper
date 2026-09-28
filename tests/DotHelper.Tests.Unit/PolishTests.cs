using DotHelper.Cli;
using DotHelper.Core.Dotnet;
using DotHelper.Ui;

using FluentAssertions;

namespace DotHelper.Tests.Unit;

/// <summary>
/// Outcome wording and clipboard plumbing (Fase 6): dry-run must never claim a past success,
/// and <c>--print-cmd</c> copying is best-effort (PLAN.md §6e).
/// </summary>
public sealed class PolishTests
{
    // ---- FormatOutcome (dry-run message) ----

    [Fact]
    public void Dry_run_outcome_is_unmistakable_and_not_past_tense()
    {
        DotnetResult result = Result(dryRun: true);

        string message = CliSupport.FormatOutcome(result, "Created src/App/App.csproj", "would create src/App/App.csproj");

        message.Should().Be("Dry-run: would create src/App/App.csproj");
        message.Should().NotContain("Created", "dry-run must not claim a completed action");
    }

    [Fact]
    public void Executed_outcome_keeps_the_success_message()
    {
        DotnetResult result = Result(dryRun: false);

        CliSupport.FormatOutcome(result, "Created src/App/App.csproj", "would create src/App/App.csproj")
            .Should()
            .Be("Created src/App/App.csproj");
    }

    [Theory]
    [InlineData("Created X", "would create X")]
    [InlineData("Added X to Y", "would add X to Y")]
    [InlineData("Removed X from Y", "would remove X from Y")]
    [InlineData("Added reference A → B", "would add reference A → B")]
    public void Dry_run_pairs_read_as_hypothetical(string success, string would)
    {
        CliSupport.FormatOutcome(Result(dryRun: true), success, would)
            .Should()
            .Be($"Dry-run: {would}");
    }

    [Fact]
    public void FormatOutcome_rejects_blank_messages()
    {
        FluentActions.Invoking(() => CliSupport.FormatOutcome(Result(dryRun: false), " ", "would x"))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => CliSupport.FormatOutcome(Result(dryRun: false), "x", ""))
            .Should().Throw<ArgumentException>();
    }

    // ---- flag propagation from the wizard root to the dispatched flow settings ----

    [Fact]
    public void InheritFrom_copies_the_common_flags()
    {
        var source = new WorkspaceCommandSettings
        {
            DryRun = true,
            Yes = true,
            Verbose = true,
            PrintCmd = true,
        };
        var target = new WorkspaceCommandSettings();

        target.InheritFrom(source);

        target.DryRun.Should().BeTrue();
        target.Yes.Should().BeTrue();
        target.Verbose.Should().BeTrue();
        target.PrintCmd.Should().BeTrue();
    }

    [Fact]
    public void ClassCommand_preset_keeps_every_common_flag()
    {
        var settings = new ItemCommandSettings
        {
            Name = "Foo",
            Query = "record",
            PrintCmd = true,
            DryRun = true,
            Verbose = true,
            Yes = true,
        };

        ItemCommandSettings preset = ClassCommand.PresetFor(settings);

        preset.Query.Should().Be("record", "an explicit query is kept");
        preset.Name.Should().Be("Foo");
        preset.PrintCmd.Should().BeTrue("--print-cmd must survive the dh class preset");
        preset.DryRun.Should().BeTrue();
        preset.Verbose.Should().BeTrue();
        preset.Yes.Should().BeTrue();
    }

    [Fact]
    public void ClassCommand_preset_defaults_the_query_to_class()
    {
        ItemCommandSettings preset = ClassCommand.PresetFor(new ItemCommandSettings());

        preset.Query.Should().Be("class");
    }

    [Fact]
    public void InheritFrom_tolerates_a_null_source()
    {
        var target = new WorkspaceCommandSettings { DryRun = true };

        target.InheritFrom(null);

        target.DryRun.Should().BeTrue("a null source leaves the target untouched");
        target.PrintCmd.Should().BeFalse();
    }

    // ---- Clipboard (pure tool selection + injectable copy) ----

    [Fact]
    public void SelectTool_prefers_wl_copy_then_pbcopy()
    {
        Clipboard.SelectTool(["pbcopy", "wl-copy"]).Should().Be("wl-copy");
        Clipboard.SelectTool(["pbcopy"]).Should().Be("pbcopy");
        Clipboard.SelectTool(["xclip"]).Should().BeNull();
        Clipboard.SelectTool(null).Should().BeNull();
        Clipboard.SelectTool([]).Should().BeNull();
    }

    [Fact]
    public void TryCopy_writes_through_the_first_available_tool()
    {
        List<string> writes = [];

        ClipboardResult result = Clipboard.TryCopy(
            "dotnet new classlib -n X",
            toolExists: t => t == "pbcopy",
            write: (tool, text) =>
            {
                writes.Add($"{tool}:{text}");
                return true;
            });

        result.Copied.Should().BeTrue();
        result.Tool.Should().Be("pbcopy");
        writes.Should().Equal("pbcopy:dotnet new classlib -n X");
    }

    [Fact]
    public void TryCopy_reports_no_tool_without_writing()
    {
        ClipboardResult result = Clipboard.TryCopy(
            "x",
            toolExists: _ => false,
            write: (_, _) => throw new InvalidOperationException("must not write"));

        result.Status.Should().Be(ClipboardStatus.NoTool);
        result.Copied.Should().BeFalse();
    }

    [Fact]
    public void TryCopy_never_throws_when_the_tool_fails()
    {
        ClipboardResult failed = Clipboard.TryCopy(
            "x",
            toolExists: t => t == "wl-copy",
            write: (_, _) => false);

        failed.Status.Should().Be(ClipboardStatus.Failed);
        failed.Tool.Should().Be("wl-copy");
        failed.Error.Should().NotBeNullOrWhiteSpace();

        // A broken clipboard (e.g. wl-copy without Wayland) surfaces as Failed, not as a crash.
        ClipboardResult crashed = Clipboard.TryCopy(
            "x",
            toolExists: t => t == "wl-copy",
            write: (_, _) => throw new System.ComponentModel.Win32Exception(2, "no display"));

        crashed.Status.Should().Be(ClipboardStatus.Failed);
        crashed.Error.Should().Contain("no display");
    }

    private static DotnetResult Result(bool dryRun) =>
        new()
        {
            ExitCode = 0,
            StdOut = string.Empty,
            StdErr = string.Empty,
            CommandLine = "dotnet new classlib -n App",
            DryRun = dryRun,
        };
}