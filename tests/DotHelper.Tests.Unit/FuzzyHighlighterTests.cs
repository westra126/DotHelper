using DotHelper.Ui;

using FluentAssertions;

namespace DotHelper.Tests.Unit;

public sealed class FuzzyHighlighterTests
{
    [Fact]
    public void Finds_contiguous_substring_case_insensitive()
    {
        FuzzyHighlighter.FindMatchPositions("Class Library", "class")
            .Should().Equal(0, 1, 2, 3, 4);

        FuzzyHighlighter.FindMatchPositions("Console App", "CON")
            .Should().Equal(0, 1, 2);
    }

    [Fact]
    public void Falls_back_to_subsequence_when_no_substring()
    {
        // "cls" is not a substring of "class"; greedy left-to-right picks c(0) l(1) s(3).
        FuzzyHighlighter.FindMatchPositions("class", "cls")
            .Should().Equal(0, 1, 3);
    }

    [Fact]
    public void Empty_query_or_text_yields_no_positions()
    {
        FuzzyHighlighter.FindMatchPositions("class", string.Empty).Should().BeEmpty();
        FuzzyHighlighter.FindMatchPositions("class", "   ").Should().BeEmpty();
        FuzzyHighlighter.FindMatchPositions(string.Empty, "cls").Should().BeEmpty();
        FuzzyHighlighter.FindMatchPositions(null, "cls").Should().BeEmpty();
        FuzzyHighlighter.FindMatchPositions("class", null).Should().BeEmpty();
    }

    [Fact]
    public void No_match_at_all_yields_no_positions()
    {
        FuzzyHighlighter.FindMatchPositions("class", "zzz").Should().BeEmpty();
        FuzzyHighlighter.FindMatchPositions("class", "claszz").Should().BeEmpty();
    }

    [Fact]
    public void Substring_hit_is_preferred_over_scattered_hit()
    {
        // "ab" occurs contiguous at 2..3 even though a scattered a…b also exists.
        FuzzyHighlighter.FindMatchPositions("xayb ab", "ab")
            .Should().Equal(5, 6);
    }

    [Fact]
    public void RenderHighlight_wraps_marked_positions()
    {
        string rendered = FuzzyPicker<string>.RenderHighlight("class", new[] { 0, 1 });

        rendered.Should().Contain("[");
        rendered.Should().Contain("]");
        // Non-matched tail must stay plain.
        rendered.Should().Contain("ass");
    }

    [Fact]
    public void RenderHighlight_escapes_markup_in_text()
    {
        string rendered = FuzzyPicker<string>.RenderHighlight("a[b]", new[] { 0 });

        rendered.Should().Contain("[["); // escaped '['
    }
}