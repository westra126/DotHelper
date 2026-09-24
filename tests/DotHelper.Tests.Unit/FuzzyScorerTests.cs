using DotHelper.Core.Dotnet;
using DotHelper.Ui;

using FluentAssertions;

namespace DotHelper.Tests.Unit;

public sealed class FuzzyScorerTests
{
    [Fact]
    public void Query_cls_ranks_class_first_among_item_templates()
    {
        IReadOnlyList<TemplateInfo> templates = LoadEn();

        IReadOnlyList<ScoredItem<TemplateInfo>> ranked = Rank(templates, "cls");

        ranked.Should().NotBeEmpty();
        ranked[0].Item.ShortNames.Should().Contain("class");

        // class (item) must beat every other item template returned.
        var otherItems = ranked.Skip(1).Where(static t => t.Item.Type == "item").ToList();
        otherItems.Should().OnlyContain(t => ranked[0].Score > t.Score);
    }

    [Fact]
    public void Query_cons_ranks_console_first()
    {
        IReadOnlyList<TemplateInfo> templates = LoadEn();

        IReadOnlyList<ScoredItem<TemplateInfo>> ranked = Rank(templates, "cons");

        ranked.Should().NotBeEmpty();
        ranked[0].Item.ShortNames.Should().Contain("console");
    }

    [Fact]
    public void Query_webapi_ranks_webapi_first()
    {
        IReadOnlyList<TemplateInfo> templates = LoadEn();

        IReadOnlyList<ScoredItem<TemplateInfo>> ranked = Rank(templates, "webapi");

        ranked.Should().NotBeEmpty();
        ranked[0].Item.ShortNames.Should().Contain("webapi");
    }

    [Fact]
    public void Query_api_returns_webapi_high()
    {
        IReadOnlyList<TemplateInfo> templates = LoadEn();

        IReadOnlyList<ScoredItem<TemplateInfo>> ranked = Rank(templates, "api");

        ranked.Should().NotBeEmpty();
        ranked.Take(3).SelectMany(static t => t.Item.ShortNames)
            .Where(static name => name is "webapi" or "webapiaot" or "apicontroller")
            .Should().NotBeEmpty();
    }

    [Fact]
    public void ShortName_match_beats_equivalent_tags_only_match()
    {
        // Identical raw ratio on both items; only the field weight (1.0 vs 0.5) may decide.
        var items = new[]
        {
            new SearchItem("via-tag", new WeightedField("xyz", WeightedField.TagWeight)),
            new SearchItem("via-shortname", new WeightedField("xyz", WeightedField.ShortNameWeight)),
        };

        IReadOnlyList<ScoredItem<SearchItem>> ranked = Rank(items, "xyz", static i => i.Fields);

        ranked.Select(static r => r.Item.Name).Should().Equal("via-shortname", "via-tag");
        ranked[0].Score.Should().BeGreaterThan(ranked[1].Score);
    }

    [Fact]
    public void Name_weight_sits_between_shortName_and_tag()
    {
        var items = new[]
        {
            new SearchItem("tag", new WeightedField("alpha", WeightedField.TagWeight)),
            new SearchItem("name", new WeightedField("alpha", WeightedField.NameWeight)),
            new SearchItem("short", new WeightedField("alpha", WeightedField.ShortNameWeight)),
        };

        IReadOnlyList<ScoredItem<SearchItem>> ranked = Rank(items, "alpha", static i => i.Fields);

        ranked.Select(static r => r.Item.Name).Should().Equal("short", "name", "tag");
    }

    [Fact]
    public void Irrelevant_query_is_empty_at_default_cutoff_but_not_at_zero()
    {
        IReadOnlyList<TemplateInfo> templates = LoadEn();

        Rank(templates, "zzzzz").Should().BeEmpty();

        IReadOnlyList<ScoredItem<TemplateInfo>> loose = FuzzyScorer.Rank(
            "zzzzz", templates, TemplateSearch.Fields, cutoff: 0);
        loose.Should().NotBeEmpty();
    }

    [Fact]
    public void Empty_query_preserves_original_order()
    {
        var items = new[]
        {
            new SearchItem("first", new WeightedField("aaa", WeightedField.ShortNameWeight)),
            new SearchItem("second", new WeightedField("bbb", WeightedField.ShortNameWeight)),
            new SearchItem("third", new WeightedField("ccc", WeightedField.ShortNameWeight)),
        };

        IReadOnlyList<ScoredItem<SearchItem>> ranked = Rank(items, string.Empty, static i => i.Fields);

        ranked.Select(static r => r.Item.Name).Should().Equal("first", "second", "third");
        ranked.Should().OnlyContain(static r => r.Score == 0);
    }

    [Fact]
    public void Whitespace_query_preserves_original_order()
    {
        var items = new[]
        {
            new SearchItem("first", new WeightedField("aaa", WeightedField.ShortNameWeight)),
            new SearchItem("second", new WeightedField("bbb", WeightedField.ShortNameWeight)),
        };

        Rank(items, "   ", static i => i.Fields).Select(static r => r.Item.Name)
            .Should().Equal("first", "second");
    }

    [Fact]
    public void Tiebreak_prefers_project_over_item_on_equal_scores()
    {
        // Identical fields → identical scores; only the tiebreak can order them.
        var items = new[]
        {
            new TypedItem("the-item", "item", new WeightedField("omega", WeightedField.ShortNameWeight)),
            new TypedItem("the-project", "project", new WeightedField("omega", WeightedField.ShortNameWeight)),
        };

        IReadOnlyList<ScoredItem<TypedItem>> ranked = FuzzyScorer.Rank(
            "omega",
            items,
            static i => i.Fields,
            cutoff: FuzzyScorer.DefaultCutoff,
            tiebreak: FuzzyScorer.TypePriority<TypedItem>(static i => i.Type));

        ranked.Select(static r => r.Item.Name).Should().Equal("the-project", "the-item");
    }

    [Fact]
    public void Without_tiebreak_equal_scores_keep_source_order()
    {
        var items = new[]
        {
            new TypedItem("first", "item", new WeightedField("omega", WeightedField.ShortNameWeight)),
            new TypedItem("second", "project", new WeightedField("omega", WeightedField.ShortNameWeight)),
        };

        IReadOnlyList<ScoredItem<TypedItem>> ranked = FuzzyScorer.Rank(
            "omega", items, static i => i.Fields);

        ranked.Select(static r => r.Item.Name).Should().Equal("first", "second");
    }

    [Fact]
    public void Ranking_is_case_insensitive()
    {
        IReadOnlyList<TemplateInfo> templates = LoadEn();

        IReadOnlyList<ScoredItem<TemplateInfo>> lower = Rank(templates, "class");
        IReadOnlyList<ScoredItem<TemplateInfo>> upper = Rank(templates, "CLASS");
        IReadOnlyList<ScoredItem<TemplateInfo>> mixed = Rank(templates, "ClAsS");

        upper.Select(static r => r.Item.Name).Should().Equal(lower.Select(static r => r.Item.Name));
        mixed.Select(static r => r.Item.Name).Should().Equal(lower.Select(static r => r.Item.Name));
        lower[0].Score.Should().Be(upper[0].Score);
    }

    [Fact]
    public void Accented_query_does_not_break()
    {
        IReadOnlyList<TemplateInfo> en = LoadEn();
        IReadOnlyList<TemplateInfo> es = LoadEs();

        // No match is required for "clase"; it must simply not throw and must stay well-defined.
        Action act = () =>
        {
            Rank(es, "clase");
            Rank(en, "clase");
            Rank(es, "aplicación");
            Rank(es, "aplicacion");
        };

        act.Should().NotThrow();

        IReadOnlyList<ScoredItem<TemplateInfo>> spanish = Rank(es, "clase");
        spanish.Should().Contain(static t => t.Item.ShortNames.Contains("class"));
    }

    [Fact]
    public void Query_with_spaces_does_not_break()
    {
        IReadOnlyList<TemplateInfo> templates = LoadEn();

        Action act = () => Rank(templates, "web api");
        act.Should().NotThrow();

        // Multi-word query must surface the web family (no strict winner is required).
        IReadOnlyList<ScoredItem<TemplateInfo>> ranked = Rank(templates, "ASP.NET Core Web API");
        ranked.Should().NotBeEmpty();
        ranked.SelectMany(static t => t.Item.ShortNames)
            .Where(static name => name is "web" or "webapi" or "webapiaot" or "mvc" or "webapp")
            .Should().NotBeEmpty();
    }

    [Fact]
    public void Multiple_fields_take_the_best_weighted_contribution()
    {
        // Weak name match (0.85) + perfect tag match (0.5) must lose to a perfect shortName match (1.0).
        var items = new[]
        {
            new SearchItem("tag-hit", new WeightedField("zzz", WeightedField.ShortNameWeight), new WeightedField("needle", WeightedField.TagWeight)),
            new SearchItem("short-hit", new WeightedField("needle", WeightedField.ShortNameWeight), new WeightedField("zzz", WeightedField.TagWeight)),
        };

        IReadOnlyList<ScoredItem<SearchItem>> ranked = Rank(items, "needle", static i => i.Fields);

        ranked[0].Item.Name.Should().Be("short-hit");
    }

    private static IReadOnlyList<ScoredItem<TemplateInfo>> Rank(
        IReadOnlyList<TemplateInfo> templates, string query) =>
        FuzzyScorer.Rank(query, templates, TemplateSearch.Fields, tiebreak: TemplateSearch.TypeTiebreak);

    private static IReadOnlyList<ScoredItem<T>> Rank<T>(
        IReadOnlyList<T> items, string query, Func<T, IReadOnlyList<WeightedField>> fields) =>
        FuzzyScorer.Rank(query, items, fields);

    private static IReadOnlyList<TemplateInfo> LoadEn() =>
        TemplateListParser.Parse(File.ReadAllText(Fixtures.Resolve("new-list-en.txt")));

    private static IReadOnlyList<TemplateInfo> LoadEs() =>
        TemplateListParser.Parse(File.ReadAllText(Fixtures.Resolve("new-list-es.txt")));

    private sealed record SearchItem(string Name, params WeightedField[] Fields);

    private sealed record TypedItem(string Name, string Type, params WeightedField[] Fields);
}