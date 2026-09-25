using Spectre.Console.Cli;

namespace DotHelper.Cli;

/// <summary>
/// Common flags shared by workspace commands (PLAN.md §5.2 <c>--dry-run</c>, plus
/// <c>--yes</c>/<c>--verbose</c> for non-interactive and diagnostic runs).
/// </summary>
public class WorkspaceCommandSettings : CommandSettings
{
    /// <summary>Print the exact <c>dotnet</c> commands without running mutating ones.</summary>
    [CommandOption("--dry-run")]
    public bool DryRun { get; init; }

    /// <summary>Non-interactive: never prompt, use provided values or defaults.</summary>
    [CommandOption("-y|--yes")]
    public bool Yes { get; init; }

    /// <summary>Write stdout/stderr bodies of every invocation to the log file.</summary>
    [CommandOption("--verbose")]
    public bool Verbose { get; init; }
}