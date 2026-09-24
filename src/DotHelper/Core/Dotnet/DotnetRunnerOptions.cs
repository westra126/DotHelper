namespace DotHelper.Core.Dotnet;

/// <summary>
/// Construction-time behavior of <see cref="DotnetRunner"/>.
/// </summary>
public sealed record DotnetRunnerOptions
{
    /// <summary>When true, no process is started; the command line is returned instead.</summary>
    public bool DryRun { get; init; }

    /// <summary>When true, stdout/stderr bodies are also written to the log file.</summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// Log directory. <c>null</c> uses <c>~/.local/state/dothelper/logs</c>
    /// (per PLAN.md §6a: file logging because Console.WriteLine breaks a TUI).
    /// </summary>
    public string? LogDirectory { get; init; }
}