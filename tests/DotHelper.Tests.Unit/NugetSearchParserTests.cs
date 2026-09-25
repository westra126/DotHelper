using DotHelper.Core.Dotnet;

using FluentAssertions;

namespace DotHelper.Tests.Unit;

public sealed class NugetSearchParserTests
{
    [Fact]
    public void Detailed_cli_json_maps_all_reported_fields()
    {
        // Real capture: `dotnet package search Newtonsoft --take 3 --format json --verbosity detailed`.
        IReadOnlyList<NugetPackageInfo> packages = ParseCliFixture("package-search-detailed.json");

        packages.Should().HaveCount(3);

        NugetPackageInfo newtonsoft = packages[0];
        newtonsoft.Id.Should().Be("Newtonsoft.Json");
        newtonsoft.LatestVersion.Should().Be("13.0.4");
        newtonsoft.TotalDownloads.Should().Be(9288626611);
        newtonsoft.Owners.Should().Be("dotnetfoundation, jamesnk, newtonsoft");
        newtonsoft.Description.Should().StartWith("Json.NET");

        newtonsoft.Verified.Should().BeNull("the CLI JSON never reports the reserved-prefix badge");
    }

    [Fact]
    public void Normal_cli_json_omits_description()
    {
        // Real capture without `--verbosity detailed`: no description / projectUrl.
        IReadOnlyList<NugetPackageInfo> packages = ParseCliFixture("package-search-plain.json");

        packages.Should().HaveCount(3);
        packages.Should().OnlyContain(static p => p.Description == null);
        packages[0].Id.Should().Be("Newtonsoft.Json");
    }

    [Fact]
    public void Empty_cli_results_yield_empty_list()
    {
        // Real capture of a search with no matches ("problems" stays empty).
        string json = File.ReadAllText(Fixtures.Resolve("package-search-empty.json"));

        json.Should().Contain("\"packages\": []");
        NugetSearchParser.ParseDotnetJson(json).Should().BeEmpty();
    }

    [Fact]
    public void Multiple_sources_are_concatenated_in_order()
    {
        const string Json = """
            {
              "version": 2,
              "problems": [],
              "searchResult": [
                { "sourceName": "nuget.org", "packages": [ { "id": "A", "latestVersion": "1.0.0", "totalDownloads": 5, "owners": "alice" } ] },
                { "sourceName": "other", "packages": [ { "id": "B", "latestVersion": "2.0.0", "totalDownloads": 7, "owners": "bob" } ] }
              ]
            }
            """;

        IReadOnlyList<NugetPackageInfo> packages = NugetSearchParser.ParseDotnetJson(Json);

        packages.Select(static p => p.Id).Should().Equal("A", "B");
    }

    [Fact]
    public void Azure_search_json_maps_version_and_joins_owners()
    {
        // Real capture from https://azuresearch-usnc.nuget.org/query (HTTP fallback path).
        IReadOnlyList<NugetPackageInfo> packages =
            NugetSearchParser.ParseAzureSearchJson(File.ReadAllText(Fixtures.Resolve("azuresearch-query.json")));

        packages.Should().ContainSingle();
        NugetPackageInfo bson = packages[0];
        bson.Id.Should().Be("Newtonsoft.Json.Bson");
        bson.LatestVersion.Should().Be("1.0.3");
        bson.Verified.Should().BeTrue("the HTTP API reports the badge");
        bson.Owners.Should().Be("dotnetfoundation, jamesnk, newtonsoft");
        bson.TotalDownloads.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Null_or_blank_json_yields_empty_list()
    {
        NugetSearchParser.ParseDotnetJson(null).Should().BeEmpty();
        NugetSearchParser.ParseDotnetJson("  ").Should().BeEmpty();
        NugetSearchParser.ParseAzureSearchJson(null).Should().BeEmpty();
    }

    [Fact]
    public void Json_without_results_key_yields_empty_list()
    {
        NugetSearchParser.ParseDotnetJson("""{ "version": 2, "problems": [] }""").Should().BeEmpty();
        NugetSearchParser.ParseAzureSearchJson("""{ "totalHits": 0 }""").Should().BeEmpty();
    }

    [Fact]
    public void Malformed_json_throws_JsonException()
    {
        FluentActions.Invoking(() => NugetSearchParser.ParseDotnetJson("{ not json"))
            .Should()
            .Throw<System.Text.Json.JsonException>();
    }

    [Fact]
    public void ToJson_round_trips_packages()
    {
        IReadOnlyList<NugetPackageInfo> packages = ParseCliFixture("package-search-plain.json");

        string json = NugetSearchParser.ToJson(packages);

        json.Should().Contain("\"id\":\"Newtonsoft.Json\"");
        json.Should().Contain("latestVersion");
    }

    private static IReadOnlyList<NugetPackageInfo> ParseCliFixture(string name) =>
        NugetSearchParser.ParseDotnetJson(File.ReadAllText(Fixtures.Resolve(name)));
}