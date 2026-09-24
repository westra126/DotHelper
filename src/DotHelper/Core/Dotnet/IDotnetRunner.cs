namespace DotHelper.Core.Dotnet;

/// <summary>
/// Single entry point for invoking the <c>dotnet</c> CLI. Injectable for testing.
/// </summary>
public interface IDotnetRunner
{
    /// <summary>
    /// Runs <c>dotnet</c> with the given arguments, capturing stdout/stderr and exit code.
    /// In dry-run mode no process is started and <see cref="DotnetResult.CommandLine"/>
    /// carries the exact command that would have run.
    /// </summary>
    /// <param name="args">Arguments after the <c>dotnet</c> executable.</param>
    /// <param name="workingDir">Working directory; <c>null</c> uses <see cref="Environment.CurrentDirectory"/>.</param>
    /// <param name="cancellationToken">Cancels the run and kills the child process.</param>
    /// <param name="onStdOutLine">Optional streaming callback, invoked per stdout line.</param>
    /// <param name="onStdErrLine">Optional streaming callback, invoked per stderr line.</param>
    Task<DotnetResult> RunAsync(
        IEnumerable<string> args,
        string? workingDir = null,
        CancellationToken cancellationToken = default,
        Action<string>? onStdOutLine = null,
        Action<string>? onStdErrLine = null);
}