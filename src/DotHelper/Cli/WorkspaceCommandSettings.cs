using Spectre.Console.Cli;

namespace DotHelper.Cli;

/// <summary>
/// Common flags shared by workspace commands (PLAN.md §5.2 <c>--dry-run</c>, plus
/// <c>--yes</c>/<c>--verbose</c> for non-interactive and diagnostic runs and <c>--print-cmd</c>
/// for clipboard copy). Settable (not <c>init</c>) so the root wizard can propagate them to the
/// dispatched flow settings.
/// </summary>
public class WorkspaceCommandSettings : CommandSettings
{
    /// <summary>Print the exact <c>dotnet</c> commands without running mutating ones.</summary>
    [CommandOption("--dry-run")]
    public bool DryRun { get; set; }

    /// <summary>Non-interactive: never prompt, use provided values or defaults.</summary>
    [CommandOption("-y|--yes")]
    public bool Yes { get; set; }

    /// <summary>Write stdout/stderr bodies of every invocation to the log file.</summary>
    [CommandOption("--verbose")]
    public bool Verbose { get; set; }

    /// <summary>
    /// Copy the equivalent <c>dotnet</c> command(s) to the clipboard at the end of the flow
    /// (PLAN.md §6e). Requires <c>wl-copy</c> or <c>pbcopy</c>; silently skipped otherwise.
    /// </summary>
    [CommandOption("--print-cmd")]
    public bool PrintCmd { get; set; }

    /// <summary>Copies the common flags from <paramref name="source"/> (used by the root wizard).</summary>
    public void InheritFrom(WorkspaceCommandSettings? source)
    {
        if (source is null)
        {
            return;
        }

        DryRun = source.DryRun;
        Yes = source.Yes;
        Verbose = source.Verbose;
        PrintCmd = source.PrintCmd;
    }
}