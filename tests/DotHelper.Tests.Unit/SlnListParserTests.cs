using DotHelper.Core.Dotnet;

using FluentAssertions;

namespace DotHelper.Tests.Unit;

public sealed class SlnListParserTests
{
    [Fact]
    public void Parses_real_output_with_two_projects()
    {
        string output = File.ReadAllText(Fixtures.Resolve("sln-list-two-projects.txt"));

        IReadOnlyList<string> projects = SlnListParser.Parse(output);

        projects.Should().Equal("a/A.csproj", "b/B.csproj");
    }

    [Fact]
    public void Empty_solution_yields_empty_list()
    {
        // Real capture: "No projects found in the solution." (localized) has no dashes row.
        string output = File.ReadAllText(Fixtures.Resolve("sln-list-empty.txt"));

        output.Should().Contain("No projects found");
        SlnListParser.Parse(output).Should().BeEmpty();
    }

    [Fact]
    public void Null_or_blank_output_yields_empty_list()
    {
        SlnListParser.Parse(null).Should().BeEmpty();
        SlnListParser.Parse(string.Empty).Should().BeEmpty();
        SlnListParser.Parse("   \n").Should().BeEmpty();
    }

    [Fact]
    public void Localized_preamble_without_dashes_yields_empty_list()
    {
        SlnListParser.Parse("Proyectos\nNo se encontraron proyectos.\n").Should().BeEmpty();
    }

    [Fact]
    public void Tolerates_crlf_and_trailing_blank_lines()
    {
        string lf = File.ReadAllText(Fixtures.Resolve("sln-list-two-projects.txt"));
        string crlf = lf.Replace("\n", "\r\n", StringComparison.Ordinal) + "\n\n";

        SlnListParser.Parse(crlf).Should().Equal("a/A.csproj", "b/B.csproj");
    }
}