using DotHelper.Core.Dotnet;
using DotHelper.Core.Workspace;

using FluentAssertions;

namespace DotHelper.Tests.Integration;

/// <summary>
/// End-to-end workspace flows with real <c>dotnet</c> (PLAN.md §7 Fase 3 criteria).
/// Trait <c>Category=Integration</c> lets CI run these separately from unit tests.
/// </summary>
[Trait("Category", "Integration")]
public sealed class WorkspaceFlowsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task End_to_end_creates_sln_project_and_class()
    {
        using TempWorkspace ws = new();
        string sln = await ws.CreateSolutionAsync("MiSln");
        string project = await ws.CreateProjectAsync("classlib", "Core", "src");
        DotnetResult add = await ws.Solutions.AddProjectAsync(sln, project, Ct);
        DotnetResult item = await ws.Items.CreateAsync("class", "Foo", Path.GetDirectoryName(project)!, outputSubdir: null, projectFilePath: project, cancellationToken: Ct);

        add.ExitCode.Should().Be(0);
        item.ExitCode.Should().Be(0);
        File.Exists(sln).Should().BeTrue();
        File.Exists(project).Should().BeTrue();
        File.Exists(Path.Combine(Path.GetDirectoryName(project)!, "Foo.cs")).Should().BeTrue();

        IReadOnlyList<string> projects = await ws.Solutions.ListProjectsAsync(sln, Ct);
        projects.Should().Contain(p => p.EndsWith("Core.csproj", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Sln_add_and_remove_project()
    {
        using TempWorkspace ws = new();
        string sln = await ws.CreateSolutionAsync();
        string project = await ws.CreateProjectAsync("classlib", "Lib", "src");

        DotnetResult add = await ws.Solutions.AddProjectAsync(sln, project, Ct);
        add.ExitCode.Should().Be(0);
        (await ws.Solutions.ListProjectsAsync(sln, Ct)).Should().ContainSingle();

        // Idempotent: adding again succeeds (empirical: exit 0 "already contains").
        DotnetResult again = await ws.Solutions.AddProjectAsync(sln, project, Ct);
        again.ExitCode.Should().Be(0);

        DotnetResult remove = await ws.Solutions.RemoveProjectAsync(sln, project, Ct);
        remove.ExitCode.Should().Be(0);
        (await ws.Solutions.ListProjectsAsync(sln, Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Add_ref_and_remove_ref_between_projects()
    {
        using TempWorkspace ws = new();
        string sln = await ws.CreateSolutionAsync();
        string core = await ws.CreateProjectAsync("classlib", "Core", "src");
        string app = await ws.CreateProjectAsync("console", "App", "src");
        await ws.Solutions.AddProjectAsync(sln, core, Ct);
        await ws.Solutions.AddProjectAsync(sln, app, Ct);

        DotnetResult addRef = await ws.Projects.AddReferenceAsync(app, core, Ct);
        addRef.ExitCode.Should().Be(0);
        File.ReadAllText(app).Should().Contain("Core.csproj", "the app project must reference Core");

        // Idempotent (empirical: exit 0 "already has a reference").
        (await ws.Projects.AddReferenceAsync(app, core, Ct)).ExitCode.Should().Be(0);

        DotnetResult removeRef = await ws.Projects.RemoveReferenceAsync(app, core, Ct);
        removeRef.ExitCode.Should().Be(0);
        File.ReadAllText(app).Should().NotContain("Core.csproj");
    }

    [Fact]
    public async Task WorkspaceLocator_detects_sln_from_subdirectory()
    {
        using TempWorkspace ws = new();
        string sln = await ws.CreateSolutionAsync("MySln");
        string project = await ws.CreateProjectAsync("classlib", "Core", "src");
        await ws.Solutions.AddProjectAsync(sln, project, Ct);

        string deepDir = Path.Combine(Path.GetDirectoryName(project)!, "Domain", "Nested");
        Directory.CreateDirectory(deepDir);

        WorkspaceLookupResult lookup = WorkspaceLocator.Locate(deepDir, boundaryDirectory: ws.Root);

        lookup.Closest.Should().NotBeNull();
        lookup.Closest!.Kind.Should().Be("csproj");
        lookup.All.Should().Contain(a => a.Kind == "sln" && a.FullPath == sln);
    }

    [Fact]
    public async Task Item_lands_in_project_subdir_via_output_fallback()
    {
        using TempWorkspace ws = new();
        string sln = await ws.CreateSolutionAsync();
        string project = await ws.CreateProjectAsync("classlib", "Core", "src");
        await ws.Solutions.AddProjectAsync(sln, project, Ct);
        string projectDir = Path.GetDirectoryName(project)!;

        // PLAN.md §5.3: item with a relative output folder must land inside the project tree.
        DotnetResult result = await ws.Items.CreateAsync(
            "class", "CustomerId", projectDir, outputSubdir: "Domain/Customers", projectFilePath: project, cancellationToken: Ct);

        result.ExitCode.Should().Be(0);
        string created = Path.Combine(projectDir, "Domain", "Customers", "CustomerId.cs");
        File.Exists(created).Should().BeTrue("the fallback -o <projectDir>/<subdir> puts the file here");
    }

    [Fact]
    public async Task Dry_run_creates_no_files()
    {
        using TempWorkspace ws = new();
        var dryRunner = new DotnetRunner(new DotnetRunnerOptions { DryRun = true });
        var dryProjects = new ProjectService(dryRunner);

        DotnetResult result = await dryProjects.CreateAsync(
            "classlib", "X", ws.PathTo("src", "X"), Ct);

        result.DryRun.Should().BeTrue();
        result.CommandLine.Should().Contain("dotnet new classlib -n X");
        result.ExitCode.Should().Be(0);

        Directory.Exists(ws.PathTo("src", "X")).Should().BeFalse();
        Directory.GetFileSystemEntries(ws.Root).Should().BeEmpty("dry-run must not create anything");
    }

    [Fact]
    public async Task Sln_list_reports_projects_with_real_parser()
    {
        using TempWorkspace ws = new();
        string sln = await ws.CreateSolutionAsync();
        string a = await ws.CreateProjectAsync("classlib", "A", "a");
        string b = await ws.CreateProjectAsync("classlib", "B", "b");
        await ws.Solutions.AddProjectAsync(sln, a, Ct);
        await ws.Solutions.AddProjectAsync(sln, b, Ct);

        IReadOnlyList<string> projects = await ws.Solutions.ListProjectsAsync(sln, Ct);

        projects.Should().HaveCount(2);
        projects.Should().OnlyContain(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));
    }
}