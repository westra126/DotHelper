using DotHelper.Core.Dotnet;

namespace DotHelper.Tests.Integration;

/// <summary>
/// Unique workspace under /tmp running real <c>dotnet</c> commands (PLAN.md §8 TempWorkspace).
/// Cleans up on dispose. Services share a real runner (never dry-run) so tests exercise the CLI.
/// </summary>
public sealed class TempWorkspace : IDisposable
{
    public TempWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "dothelper-it-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Runner = new DotnetRunner();
        Solutions = new SolutionService(Runner);
        Projects = new ProjectService(Runner);
        Items = new ItemService(Runner);
        Packages = new NugetService(Runner);
    }

    public string Root { get; }

    public IDotnetRunner Runner { get; }

    public SolutionService Solutions { get; }

    public ProjectService Projects { get; }

    public ItemService Items { get; }

    public NugetService Packages { get; }

    /// <summary>Test cancellation token (xunit.v3).</summary>
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Absolute path under the workspace root.</summary>
    public string PathTo(params string[] parts) =>
        Path.Combine(new[] { Root }.Concat(parts).ToArray());

    /// <summary>Creates a classic .sln in the workspace root and returns its full path.</summary>
    public async Task<string> CreateSolutionAsync(string name = "App")
    {
        // outputDir must be the workspace root: `dotnet new sln` writes relative to cwd otherwise.
        DotnetResult result = await Solutions.CreateAsync(name, outputDir: Root, SlnFormat.Sln, Ct);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"sln create failed: {result.StdErr}\n{result.StdOut}");
        }

        return PathTo(name + ".sln");
    }

    /// <summary>Creates a project at <c>&lt;parent&gt;/&lt;name&gt;</c> and returns the csproj path.</summary>
    public async Task<string> CreateProjectAsync(string template, string name, string parent)
    {
        string projectDir = PathTo(parent, name);
        Directory.CreateDirectory(projectDir);

        DotnetResult result = await Projects.CreateAsync(template, name, projectDir, Ct);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"project create failed: {result.StdErr}\n{result.StdOut}");
        }

        return Path.Combine(projectDir, name + ".csproj");
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}