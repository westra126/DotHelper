using DotHelper.Core.Dotnet;

using FluentAssertions;

namespace DotHelper.Tests.Integration;

/// <summary>
/// Real <c>dotnet</c> NuGet flows on a temp project (PLAN.md §7 Fase 4 criteria:
/// add/remove package on a temp csproj). Trait <c>Category=Integration</c> lets CI run these
/// separately from unit tests. Network access to nuget.org is required.
/// </summary>
[Trait("Category", "Integration")]
public sealed class NugetFlowsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Add_list_and_remove_package_round_trip()
    {
        using TempWorkspace ws = new();
        string project = await ws.CreateProjectAsync("classlib", "Lib", "src");

        DotnetResult add = await ws.Packages.AddAsync(project, "Newtonsoft.Json", cancellationToken: Ct);
        add.ExitCode.Should().Be(0, "dotnet add package without --version resolves the latest stable version");

        IReadOnlyList<InstalledPackage> installed = await ws.Packages.ListAsync(project, cancellationToken: Ct);
        InstalledPackage newtonsoft = installed.Should().ContainSingle(p => p.Id == "Newtonsoft.Json").Subject;
        newtonsoft.IsTransitive.Should().BeFalse();
        newtonsoft.Resolved.Should().NotBeNullOrWhiteSpace();
        newtonsoft.Requested.Should().Be(newtonsoft.Resolved, "no version pin → requested equals resolved");

        DotnetResult remove = await ws.Packages.RemoveAsync(project, "Newtonsoft.Json", Ct);
        remove.ExitCode.Should().Be(0);

        (await ws.Packages.ListAsync(project, cancellationToken: Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Add_with_pinned_version_resolves_that_version()
    {
        using TempWorkspace ws = new();
        string project = await ws.CreateProjectAsync("classlib", "Lib", "src");

        DotnetResult add = await ws.Packages.AddAsync(project, "Newtonsoft.Json", "12.0.3", Ct);
        add.ExitCode.Should().Be(0);

        IReadOnlyList<InstalledPackage> installed = await ws.Packages.ListAsync(project, cancellationToken: Ct);
        InstalledPackage newtonsoft = installed.Should().ContainSingle(p => p.Id == "Newtonsoft.Json").Subject;
        newtonsoft.Requested.Should().Be("12.0.3");
        newtonsoft.Resolved.Should().Be("12.0.3");
    }

    [Fact]
    public async Task Search_real_query_returns_at_most_take_results()
    {
        using TempWorkspace ws = new();

        IReadOnlyList<NugetPackageInfo> packages = await ws.Packages
            .SearchAsync("Newtonsoft", take: 3, cancellationToken: Ct);

        packages.Should().NotBeEmpty();
        packages.Count.Should().BeLessThanOrEqualTo(3);
        packages[0].Id.Should().Contain("Newtonsoft", "the first hit for 'Newtonsoft' is Newtonsoft.Json");
        packages[0].LatestVersion.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Remove_of_a_package_that_is_not_installed_fails()
    {
        using TempWorkspace ws = new();
        string project = await ws.CreateProjectAsync("classlib", "Lib", "src");

        DotnetResult remove = await ws.Packages
            .RemoveAsync(project, "This.Package.Does.Not.Exist.12345", Ct);

        remove.ExitCode.Should().NotBe(0, "empirical: removing a missing PackageReference exits 1");
        NugetService.DescribeError(remove)
            .Should()
            .Contain("does not contain any PackageReference", "the friendly message comes from the CLI wording");
    }

    [Fact]
    public async Task List_of_unknown_project_raises_a_friendly_error()
    {
        using TempWorkspace ws = new();

        await FluentActions.Awaiting(() => ws.Packages.ListAsync(ws.PathTo("Nope.csproj"), cancellationToken: Ct))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Could not find*");
    }
}