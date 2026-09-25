using DotHelper.Core.Dotnet;

using FluentAssertions;

namespace DotHelper.Tests.Unit;

public sealed class PackageListParserTests
{
    [Fact]
    public void En_fixture_parses_top_level_and_transitive_rows()
    {
        IReadOnlyList<InstalledPackage> packages = ParseFixture("list-package-en.txt");

        packages.Should().HaveCount(8);

        InstalledPackage logging = ById(packages, "Microsoft.Extensions.Logging");
        logging.IsTransitive.Should().BeFalse();
        logging.Requested.Should().Be("10.0.12");
        logging.Resolved.Should().Be("10.0.12");

        InstalledPackage options = ById(packages, "Microsoft.Extensions.Options");
        options.IsTransitive.Should().BeTrue();
        options.Requested.Should().BeNull("transitive packages have no request");
        options.Resolved.Should().Be("10.0.12");
    }

    [Fact]
    public void En_and_es_fixtures_yield_the_same_rows()
    {
        IReadOnlyList<InstalledPackage> en = ParseFixture("list-package-en.txt");
        IReadOnlyList<InstalledPackage> es = ParseFixture("list-package-es.txt");

        en.Should().NotBeEmpty();
        es.Select(Describe).Should().Equal(en.Select(Describe), "headers are localized but rows must parse identically");
    }

    [Fact]
    public void Requested_differs_from_resolved_for_floating_and_range_versions()
    {
        IReadOnlyList<InstalledPackage> packages = ParseFixture("list-package-en.txt");

        InstalledPackage floating = ById(packages, "Newtonsoft.Json");
        floating.Requested.Should().Be("13.*");
        floating.Resolved.Should().Be("13.0.4", "the floating request resolves to the latest matching version");

        // A requested range contains a single space; it must survive as one cell.
        InstalledPackage range = ById(packages, "NuGet.Versioning");
        range.Requested.Should().Be("[6.0, 7.0)");
        range.Resolved.Should().Be("6.0.0");
    }

    [Fact]
    public void Empty_project_yields_empty_list()
    {
        // Real capture: "No packages were found for this framework." (localized) — no rows.
        string output = File.ReadAllText(Fixtures.Resolve("list-package-empty.txt"));

        output.Should().Contain("No packages were found");
        PackageListParser.Parse(output).Should().BeEmpty();
    }

    [Fact]
    public void Multi_targeted_project_repeats_rows_per_framework()
    {
        IReadOnlyList<InstalledPackage> packages = ParseFixture("list-package-multitfm.txt");

        packages.Should().HaveCount(2);
        packages.Should().OnlyContain(static p => p.Id == "Newtonsoft.Json");
        packages.Should().OnlyContain(static p => !p.IsTransitive);
    }

    [Fact]
    public void Null_or_blank_output_yields_empty_list()
    {
        PackageListParser.Parse(null).Should().BeEmpty();
        PackageListParser.Parse(string.Empty).Should().BeEmpty();
        PackageListParser.Parse("   \n  ").Should().BeEmpty();
    }

    [Fact]
    public void Localized_preamble_without_rows_yields_empty_list()
    {
        PackageListParser.Parse("El proyecto \"App\" tiene las referencias de paquete siguientes\n   [net8.0]: No se encontró ningún paquete para este marco.\n")
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Tolerates_crlf_and_trailing_blank_lines()
    {
        string lf = File.ReadAllText(Fixtures.Resolve("list-package-en.txt"));
        string crlf = lf.Replace("\n", "\r\n", StringComparison.Ordinal) + "\n\n";

        IReadOnlyList<InstalledPackage> fromLf = PackageListParser.Parse(lf);
        IReadOnlyList<InstalledPackage> fromCrlf = PackageListParser.Parse(crlf);

        fromCrlf.Select(Describe).Should().Equal(fromLf.Select(Describe));
    }

    [Fact]
    public void Row_without_resolved_column_is_skipped()
    {
        PackageListParser.Parse("> Lonely.Id\n").Should().BeEmpty();
    }

    [Fact]
    public void Extra_columns_re_join_as_requested_range()
    {
        // Defensive: if a range were split by an internal 2+ space run, the middle re-joins
        // (normalized to single spaces).
        IReadOnlyList<InstalledPackage> packages = PackageListParser.Parse("   > Weird.Id      [6.0,  7.0)   6.1.0\n");

        packages.Should().ContainSingle();
        packages[0].Requested.Should().Be("[6.0, 7.0)");
        packages[0].Resolved.Should().Be("6.1.0");
        packages[0].IsTransitive.Should().BeFalse();
    }

    private static IReadOnlyList<InstalledPackage> ParseFixture(string name) =>
        PackageListParser.Parse(File.ReadAllText(Fixtures.Resolve(name)));

    private static InstalledPackage ById(IReadOnlyList<InstalledPackage> packages, string id) =>
        packages.Single(p => p.Id == id);

    private static string Describe(InstalledPackage package) =>
        $"{package.Id}|{package.Requested}|{package.Resolved}|{package.IsTransitive}";
}