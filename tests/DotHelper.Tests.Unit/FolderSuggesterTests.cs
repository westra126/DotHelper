using DotHelper.Cli;
using DotHelper.Core.Workspace;

using FluentAssertions;

namespace DotHelper.Tests.Unit;

public sealed class FolderSuggesterTests
{
    [Theory]
    [InlineData("classlib", true, "src")]
    [InlineData("console", true, "src")]
    [InlineData("web", true, "src")]
    [InlineData("classlib", false, "")]
    public void Suggests_src_when_a_solution_exists(string template, bool hasSolution, string expected)
    {
        FolderSuggester.SuggestProjectFolder(hasSolution, template).Should().Be(expected);
    }

    [Theory]
    [InlineData("xunit")]
    [InlineData("nunit")]
    [InlineData("mstest")]
    [InlineData("mstest-playwright")]
    [InlineData("nunit-playwright")]
    public void Suggests_tests_for_test_templates(string template)
    {
        FolderSuggester.SuggestProjectFolder(hasSolution: true, template).Should().Be("tests");
        FolderSuggester.SuggestProjectFolder(hasSolution: false, template).Should().Be("tests");
    }

    [Theory]
    [InlineData("xunit", true)]
    [InlineData("nunit", true)]
    [InlineData("mstest", true)]
    [InlineData("MSTest", true)]
    [InlineData("classlib", false)]
    [InlineData("console", false)]
    [InlineData("worker", false)]
    [InlineData("", false)]
    public void Detects_test_templates(string template, bool expected)
    {
        FolderSuggester.LooksLikeTestTemplate(template).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "Core", "Core")]
    [InlineData("", "Core", "Core")]
    [InlineData("src", "Core", "src/Core")]
    [InlineData("src/App", "Core", "src/App/Core")]
    public void ComposeProjectDirectory_treats_output_as_parent_folder(string? output, string name, string expected)
    {
        CliSupport.ComposeProjectDirectory(output, name).Replace('\\', '/').Should().Be(expected);
    }
}