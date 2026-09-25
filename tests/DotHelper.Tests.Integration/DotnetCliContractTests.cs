using DotHelper.Core.Dotnet;

using FluentAssertions;

namespace DotHelper.Tests.Integration;

/// <summary>
/// Service-level behaviors documenting the empirical contract of the <c>dotnet</c> CLI
/// (idempotency, output-path rules). Kept as separate tests so SDK behavior regressions are visible.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DotnetCliContractTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Item_template_output_is_controlled_by_dash_o_not_by_dash_dash_project()
    {
        // Empirical finding: --project is context-only; -o decides the file location.
        using TempWorkspace ws = new();
        await ws.CreateSolutionAsync();
        string project = await ws.CreateProjectAsync("classlib", "Lib", "src");
        string projectDir = Path.GetDirectoryName(project)!;

        DotnetResult result = await ws.Items.CreateAsync(
            "class", "Outside", projectDir, outputSubdir: "Sub/Dir", projectFilePath: project, cancellationToken: Ct);

        result.ExitCode.Should().Be(0);
        File.Exists(Path.Combine(projectDir, "Sub", "Dir", "Outside.cs")).Should().BeTrue();
        File.Exists(Path.Combine(ws.Root, "Sub", "Dir", "Outside.cs"))
            .Should().BeFalse("the file must land under the project dir, not cwd");
    }

    [Fact]
    public async Task Item_template_succeeds_inside_project_tree_without_dash_dash_project()
    {
        using TempWorkspace ws = new();
        await ws.CreateSolutionAsync();
        string project = await ws.CreateProjectAsync("classlib", "Lib", "src");
        string projectDir = Path.GetDirectoryName(project)!;

        DotnetResult result = await ws.Items.CreateAsync(
            "record", "MyRec", projectDir, outputSubdir: "Domain", projectFilePath: null, cancellationToken: Ct);

        result.ExitCode.Should().Be(0, "-o inside the project tree satisfies the 'inside project' constraint");
        File.Exists(Path.Combine(projectDir, "Domain", "MyRec.cs")).Should().BeTrue();
    }

    [Fact]
    public async Task Sln_remove_of_missing_project_is_not_an_error()
    {
        using TempWorkspace ws = new();
        string sln = await ws.CreateSolutionAsync();

        DotnetResult result = await ws.Solutions.RemoveProjectAsync(sln, ws.PathTo("nope", "Nope.csproj"), Ct);

        result.ExitCode.Should().Be(0, "empirical: dotnet sln remove of a missing project exits 0 with a message");
    }

    [Fact]
    public async Task Add_ref_to_missing_project_fails()
    {
        using TempWorkspace ws = new();
        string project = await ws.CreateProjectAsync("classlib", "Lib", "src");

        DotnetResult result = await ws.Projects.AddReferenceAsync(project, ws.PathTo("nope", "Nope.csproj"), Ct);

        result.ExitCode.Should().NotBe(0);
    }
}