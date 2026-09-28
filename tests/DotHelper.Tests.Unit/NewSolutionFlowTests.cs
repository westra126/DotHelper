using DotHelper.Cli;
using DotHelper.Core.Dotnet;

using FluentAssertions;

namespace DotHelper.Tests.Unit;

/// <summary>
/// M2 review: <c>--format</c> parsing is a pure function raising a friendly error that the
/// global handler maps to exit 1 (<c>dh new solution X --format wtf</c>).
/// </summary>
public sealed class NewSolutionFlowTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sln")]
    [InlineData("SLN")]
    [InlineData("  sln  ")]
    public void ParseFormat_accepts_the_classic_sln(string? format)
    {
        NewSolutionFlow.ParseFormat(format).Should().Be(SlnFormat.Sln);
    }

    [Theory]
    [InlineData("slnx")]
    [InlineData("SlnX")]
    public void ParseFormat_accepts_slnx(string? format)
    {
        NewSolutionFlow.ParseFormat(format).Should().Be(SlnFormat.Slnx);
    }

    [Theory]
    [InlineData("wtf")]
    [InlineData("json")]
    public void ParseFormat_rejects_unknown_values_with_a_friendly_message(string format)
    {
        Action act = () => NewSolutionFlow.ParseFormat(format);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*Unknown solution format '{format}'*Use 'sln' or 'slnx'*");
    }
}