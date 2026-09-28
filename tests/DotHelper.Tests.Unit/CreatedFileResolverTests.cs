using DotHelper.Core.Workspace;

using FluentAssertions;

namespace DotHelper.Tests.Unit;

/// <summary>
/// Fase 6 review menores 5/6: resolving the file created by <c>dotnet new</c> must be
/// deterministic and must never treat a user name as a glob pattern.
/// </summary>
public sealed class CreatedFileResolverTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "dothelper-resolver-" + Guid.NewGuid().ToString("N"));

    public CreatedFileResolverTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void FindProjectFile_prefers_csproj_then_fsproj_then_vbproj()
    {
        File.WriteAllText(Path.Combine(_directory, "P.vbproj"), "");
        File.WriteAllText(Path.Combine(_directory, "P.fsproj"), "");
        File.WriteAllText(Path.Combine(_directory, "P.csproj"), "");

        string? found = CreatedFileResolver.FindProjectFile(_directory, "P");

        found.Should().Be(Path.Combine(_directory, "P.csproj"));
    }

    [Fact]
    public void FindProjectFile_falls_back_to_fsproj_and_vbproj()
    {
        File.WriteAllText(Path.Combine(_directory, "P.fsproj"), "");
        CreatedFileResolver.FindProjectFile(_directory, "P")
            .Should().Be(Path.Combine(_directory, "P.fsproj"));

        File.Delete(Path.Combine(_directory, "P.fsproj"));
        File.WriteAllText(Path.Combine(_directory, "P.vbproj"), "");
        CreatedFileResolver.FindProjectFile(_directory, "P")
            .Should().Be(Path.Combine(_directory, "P.vbproj"));
    }

    [Fact]
    public void FindProjectFile_returns_null_when_absent()
    {
        File.WriteAllText(Path.Combine(_directory, "Other.csproj"), "");

        CreatedFileResolver.FindProjectFile(_directory, "P").Should().BeNull();
        CreatedFileResolver.FindProjectFile(Path.Combine(_directory, "missing"), "P").Should().BeNull();
    }

    [Fact]
    public void FindByBaseName_matches_exactly_without_glob_semantics()
    {
        File.WriteAllText(Path.Combine(_directory, "P.cs"), "");
        File.WriteAllText(Path.Combine(_directory, "P1.cs"), "");
        File.WriteAllText(Path.Combine(_directory, "PP.cs"), "");

        // A name like "P*" must never behave as a wildcard.
        CreatedFileResolver.FindByBaseName(_directory, "P").Should().Be(Path.Combine(_directory, "P.cs"));
        CreatedFileResolver.FindByBaseName(_directory, "P*").Should().BeNull();
        CreatedFileResolver.FindByBaseName(_directory, "Missing").Should().BeNull();
    }

    [Fact]
    public void FindByBaseName_is_deterministic_on_multiple_matches()
    {
        File.WriteAllText(Path.Combine(_directory, "P.cs"), "");
        File.WriteAllText(Path.Combine(_directory, "P.vb"), "");

        // Both match the base name; the ordinal-first file wins every time.
        string? first = CreatedFileResolver.FindByBaseName(_directory, "P");
        string? again = CreatedFileResolver.FindByBaseName(_directory, "P");

        first.Should().Be(again);
        first.Should().Be(Path.Combine(_directory, "P.cs"), "'.cs' < '.vb' in ordinal order");
    }
}