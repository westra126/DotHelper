using System.Text.Json;

using DotHelper.Core.Dotnet;

using FluentAssertions;

using NSubstitute;

namespace DotHelper.Tests.Unit;

public sealed class TemplateListParserTests
{
    [Fact]
    public void En_and_es_fixtures_yield_the_same_template_count()
    {
        IReadOnlyList<TemplateInfo> en = ParseFixture("new-list-en.txt");
        IReadOnlyList<TemplateInfo> es = ParseFixture("new-list-es.txt");

        en.Should().NotBeEmpty();
        en.Should().HaveCount(es.Count);
    }

    [Fact]
    public void En_fixture_contains_class_console_classlib_with_expected_types()
    {
        IReadOnlyList<TemplateInfo> en = ParseFixture("new-list-en.txt");

        TemplateInfo classLib = ByShortName(en, "class");
        classLib.Type.Should().Be("item");
        classLib.Name.Should().Be("Class");

        TemplateInfo console = ByShortName(en, "console");
        console.Type.Should().Be("project");
        console.Name.Should().Be("Console App");

        TemplateInfo classLibrary = ByShortName(en, "classlib");
        classLibrary.Type.Should().Be("project");
        classLibrary.Name.Should().Be("Class Library");
    }

    [Fact]
    public void Es_fixture_contains_class_console_classlib_with_expected_types()
    {
        IReadOnlyList<TemplateInfo> es = ParseFixture("new-list-es.txt");

        // Short names are locale-agnostic; display names are not.
        ByShortName(es, "class").Type.Should().Be("item");
        ByShortName(es, "console").Type.Should().Be("project");
        ByShortName(es, "classlib").Type.Should().Be("project");

        ByShortName(es, "class").Name.Should().Be("Clase");
        ByShortName(es, "console").Name.Should().Be("Aplicación de consola");
    }

    [Fact]
    public void Empty_stdout_fixture_returns_empty_list()
    {
        // Real "no matches" capture: stdout is empty, message goes to stderr, exit code 103.
        string stdout = File.ReadAllText(Fixtures.Resolve("new-list-empty.txt"));

        stdout.Should().BeEmpty();
        TemplateListParser.Parse(stdout).Should().BeEmpty();
    }

    [Fact]
    public void Null_or_whitespace_output_returns_empty_list()
    {
        TemplateListParser.Parse(null).Should().BeEmpty();
        TemplateListParser.Parse(string.Empty).Should().BeEmpty();
        TemplateListParser.Parse("   \n  ").Should().BeEmpty();
    }

    [Fact]
    public void Output_without_dashes_separator_returns_empty_list()
    {
        // Locale-agnostic: only the dashes row delimits the table. Preamble text is never matched.
        const string Output = "Some localized preamble\n\nAnother line with - a stray dash\n\n";

        TemplateListParser.Parse(Output).Should().BeEmpty();
    }

    [Fact]
    public void Tolerates_trailing_blank_lines()
    {
        IReadOnlyList<TemplateInfo> en = ParseFixture("new-list-en.txt");

        en.Should().NotBeEmpty();
        en.Should().OnlyContain(static t => t.Name.Length > 0);
    }

    [Fact]
    public void Tolerates_crlf_line_endings()
    {
        string lf = File.ReadAllText(Fixtures.Resolve("new-list-en.txt"));
        string crlf = lf.Replace("\n", "\r\n", StringComparison.Ordinal);

        IReadOnlyList<TemplateInfo> fromLf = TemplateListParser.Parse(lf);
        IReadOnlyList<TemplateInfo> fromCrlf = TemplateListParser.Parse(crlf);

        fromCrlf.Should().HaveCount(fromLf.Count);
        fromCrlf.Select(static t => t.Name).Should().Equal(fromLf.Select(static t => t.Name));
    }

    [Fact]
    public void Splits_short_names_languages_and_tags()
    {
        IReadOnlyList<TemplateInfo> en = ParseFixture("new-list-en.txt");

        TemplateInfo classLibrary = ByShortName(en, "classlib");
        classLibrary.ShortNames.Should().Equal("classlib");
        classLibrary.Languages.Should().Equal("C#", "F#", "VB");
        classLibrary.Tags.Should().Equal("Common", "Library");

        TemplateInfo razorApp = ByShortName(en, "webapp");
        razorApp.ShortNames.Should().Equal("webapp", "razor");

        TemplateInfo gitattributes = ByShortName(en, "gitattributes");
        gitattributes.ShortNames.Should().Equal("gitattributes", ".gitattributes");
        gitattributes.Languages.Should().BeEmpty();
        gitattributes.Tags.Should().Equal("Config");
    }

    [Fact]
    public void Tolerates_truncated_display_names()
    {
        IReadOnlyList<TemplateInfo> en = ParseFixture("new-list-en.txt");

        en.Should().Contain(static t => t.Name.Contains("...", StringComparison.Ordinal));
    }

    [Fact]
    public void Tolerates_rows_with_empty_type()
    {
        IReadOnlyList<TemplateInfo> en = ParseFixture("new-list-en.txt");

        TemplateInfo solution = ByShortName(en, "sln");
        solution.Type.Should().BeEmpty();
        solution.Name.Should().Be("Solution File");
        solution.ShortNames.Should().Equal("sln", "solution");
        solution.Author.Should().Be("Microsoft");
    }

    private static IReadOnlyList<TemplateInfo> ParseFixture(string name) =>
        TemplateListParser.Parse(File.ReadAllText(Fixtures.Resolve(name)));

    private static TemplateInfo ByShortName(IReadOnlyList<TemplateInfo> templates, string shortName) =>
        templates.Single(t => t.ShortNames.Contains(shortName));
}