using DotHelper.Core.Workspace;

using FluentAssertions;

namespace DotHelper.Tests.Unit;

public sealed class WorkspaceLocatorTests
{
    [Fact]
    public void Finds_closest_project_from_a_nested_directory()
    {
        string root = CreateTempRoot();
        try
        {
            // root/App.sln
            // root/src/App.csproj
            // root/src/App.Domain/App.Domain.csproj ← start
            File.WriteAllText(Path.Combine(root, "App.sln"), string.Empty);
            string srcDir = Path.Combine(root, "src");
            Directory.CreateDirectory(srcDir);
            File.WriteAllText(Path.Combine(srcDir, "App.csproj"), "<Project />");
            string domainDir = Path.Combine(srcDir, "App.Domain");
            Directory.CreateDirectory(domainDir);
            File.WriteAllText(Path.Combine(domainDir, "App.Domain.csproj"), "<Project />");

            WorkspaceLookupResult result = WorkspaceLocator.Locate(domainDir, boundaryDirectory: root);

            result.Closest.Should().NotBeNull();
            result.Closest!.Kind.Should().Be("csproj");
            result.Closest.FullPath.Should().Be(Path.Combine(domainDir, "App.Domain.csproj"));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Collects_full_list_up_to_and_including_the_solution()
    {
        string root = CreateTempRoot();
        try
        {
            // root/App.sln
            // root/src/App.csproj                 ← ancestor level of the start dir
            // root/src/App.Domain/App.Domain.csproj ← start
            File.WriteAllText(Path.Combine(root, "App.sln"), string.Empty);
            string srcDir = Path.Combine(root, "src");
            Directory.CreateDirectory(srcDir);
            File.WriteAllText(Path.Combine(srcDir, "App.csproj"), "<Project />");
            string domainDir = Path.Combine(srcDir, "App.Domain");
            Directory.CreateDirectory(domainDir);
            File.WriteAllText(Path.Combine(domainDir, "App.Domain.csproj"), "<Project />");

            WorkspaceLookupResult result = WorkspaceLocator.Locate(domainDir, boundaryDirectory: root);

            result.All.Should().HaveCount(3);
            result.All.Select(static a => a.Kind).Should().Equal("csproj", "csproj", "sln");
            result.All.Select(static a => Path.GetFileName(a.FullPath))
                .Should().Equal("App.Domain.csproj", "App.csproj", "App.sln");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Prefers_sln_over_csproj_in_the_same_directory()
    {
        string root = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "App.sln"), string.Empty);
            File.WriteAllText(Path.Combine(root, "App.csproj"), "<Project />");

            WorkspaceLookupResult result = WorkspaceLocator.Locate(root, boundaryDirectory: root);

            result.Closest!.Kind.Should().Be("sln");
            result.All.Should().HaveCount(2);
            result.All[1].Kind.Should().Be("csproj");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Recognizes_slnx_csproj_and_fsproj_kinds()
    {
        string root = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "App.slnx"), "<Solution />");
            File.WriteAllText(Path.Combine(root, "Lib.fsproj"), "<Project />");

            WorkspaceLookupResult result = WorkspaceLocator.Locate(root, boundaryDirectory: root);

            result.All.Select(static a => a.Kind).Should().Equal("slnx", "fsproj");
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Returns_empty_result_when_nothing_is_found()
    {
        string root = CreateTempRoot();
        try
        {
            string empty = Path.Combine(root, "empty");
            Directory.CreateDirectory(empty);

            WorkspaceLookupResult result = WorkspaceLocator.Locate(empty, boundaryDirectory: root);

            result.Closest.Should().BeNull();
            result.All.Should().BeEmpty();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    [Fact]
    public void Stops_at_boundary_directory_even_without_a_solution()
    {
        string root = CreateTempRoot();
        try
        {
            string projectDir = Path.Combine(root, "MyApp");
            Directory.CreateDirectory(projectDir);
            File.WriteAllText(Path.Combine(projectDir, "MyApp.csproj"), "<Project />");

            // Without a .sln the walk must not leave the boundary (root), so the
            // closest csproj is found and nothing above root leaks into All.
            WorkspaceLookupResult result = WorkspaceLocator.Locate(projectDir, boundaryDirectory: root);

            result.Closest!.FullPath.Should().Be(Path.Combine(projectDir, "MyApp.csproj"));
            result.All.Should().ContainSingle();
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "dothelper-ws-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}