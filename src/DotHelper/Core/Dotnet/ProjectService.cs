namespace DotHelper.Core.Dotnet;

/// <summary>
/// Project operations wrapping <c>dotnet new &lt;template&gt;</c> and
/// <c>dotnet add/remove reference</c> (PLAN.md §4 ProjectService).
/// </summary>
public sealed class ProjectService
{
    private readonly IDotnetRunner _runner;

    public ProjectService(IDotnetRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    /// <summary>
    /// <c>dotnet new &lt;shortName&gt; -n &lt;name&gt; -o &lt;projectDir&gt;</c>.
    /// <paramref name="projectDirectory"/> is the project root itself (empirically, <c>-o</c>
    /// is the directory that will contain the new <c>.csproj</c>).
    /// </summary>
    public Task<DotnetResult> CreateAsync(
        string templateShortName,
        string name,
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateShortName);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);

        return _runner.RunAsync(
            new[] { "new", templateShortName, "-n", name, "-o", projectDirectory },
            workingDir: null,
            cancellationToken: cancellationToken);
    }

    /// <summary><c>dotnet add &lt;project&gt; reference &lt;target&gt;</c>. Idempotent (exit 0 if already referenced).</summary>
    public Task<DotnetResult> AddReferenceAsync(
        string projectPath,
        string targetProjectPath,
        CancellationToken cancellationToken = default) =>
        _runner.RunAsync(
            new[] { "add", projectPath, "reference", targetProjectPath },
            workingDir: null,
            cancellationToken: cancellationToken);

    /// <summary><c>dotnet remove &lt;project&gt; reference &lt;target&gt;</c>. Missing references exit 0 with a message.</summary>
    public Task<DotnetResult> RemoveReferenceAsync(
        string projectPath,
        string targetProjectPath,
        CancellationToken cancellationToken = default) =>
        _runner.RunAsync(
            new[] { "remove", projectPath, "reference", targetProjectPath },
            workingDir: null,
            cancellationToken: cancellationToken);
}