namespace DotHelper.Core.Dotnet;

/// <summary>Output format of a new solution. SDK 10 defaults to <c>slnx</c>; we prefer classic <c>sln</c>.</summary>
public enum SlnFormat
{
    Sln,
    Slnx,
}

/// <summary>
/// Solution operations wrapping <c>dotnet new sln</c> / <c>dotnet sln</c> (PLAN.md §4 SolutionService).
/// </summary>
public sealed class SolutionService
{
    private readonly IDotnetRunner _runner;

    public SolutionService(IDotnetRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    /// <summary>
    /// <c>dotnet new sln -n &lt;name&gt; [-o &lt;dir&gt;] --format sln|slnx</c>.
    /// <c>--format</c> is always passed because SDK 10 defaults to <c>slnx</c>.
    /// </summary>
    public Task<DotnetResult> CreateAsync(
        string name,
        string? outputDir,
        SlnFormat format,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        List<string> args = new() { "new", "sln", "-n", name };
        if (!string.IsNullOrWhiteSpace(outputDir))
        {
            args.Add("-o");
            args.Add(outputDir);
        }

        args.Add("--format");
        args.Add(format == SlnFormat.Slnx ? "slnx" : "sln");

        return _runner.RunAsync(args, workingDir: null, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// <c>dotnet sln &lt;sln&gt; list</c> parsed into project paths
    /// (relative to the solution directory, as printed by the CLI).
    /// </summary>
    public async Task<IReadOnlyList<string>> ListProjectsAsync(
        string solutionPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);

        DotnetResult result = await _runner
            .RunAsync(new[] { "sln", solutionPath, "list" }, workingDir: null, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return SlnListParser.Parse(result.StdOut);
    }

    /// <summary>
    /// <c>dotnet sln &lt;sln&gt; add &lt;proj&gt;</c>. Idempotent: already-added projects exit 0
    /// ("already contains project").
    /// </summary>
    public Task<DotnetResult> AddProjectAsync(
        string solutionPath,
        string projectPath,
        CancellationToken cancellationToken = default) =>
        _runner.RunAsync(
            new[] { "sln", solutionPath, "add", projectPath },
            workingDir: null,
            cancellationToken: cancellationToken);

    /// <summary>
    /// <c>dotnet sln &lt;sln&gt; remove &lt;proj&gt;</c>. Missing projects exit 0 with a message.
    /// </summary>
    public Task<DotnetResult> RemoveProjectAsync(
        string solutionPath,
        string projectPath,
        CancellationToken cancellationToken = default) =>
        _runner.RunAsync(
            new[] { "sln", solutionPath, "remove", projectPath },
            workingDir: null,
            cancellationToken: cancellationToken);
}