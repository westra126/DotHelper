namespace DotHelper.Core.Dotnet;

/// <summary>
/// Immutable outcome of a <c>dotnet</c> invocation (real or dry-run).
/// </summary>
public sealed record DotnetResult
{
    public required int ExitCode { get; init; }

    public required string StdOut { get; init; }

    public required string StdErr { get; init; }

    /// <summary>Full command line as it was (or would be) executed, e.g. <c>dotnet new list</c>.</summary>
    public required string CommandLine { get; init; }

    /// <summary>True when no process was started because dry-run mode was active.</summary>
    public required bool DryRun { get; init; }
}