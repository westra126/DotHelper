using FluentAssertions;

namespace DotHelper.Tests.Unit;

public sealed class SanityTests
{
    [Fact]
    public void Pipeline_is_healthy()
    {
        bool ok = true;

        ok.Should().BeTrue();
    }
}