using DotHelper.Ui;

using FluentAssertions;

using Spectre.Console.Testing;

namespace DotHelper.Tests.Unit;

/// <summary>
/// <see cref="AppHeader"/> (universal-header report): the banner is defined ONCE and rendered
/// by every interactive surface (menus, pickers, prompts) without call-site duplication.
/// </summary>
public sealed class AppHeaderTests
{
    [Fact]
    public void Lines_render_the_canonical_banner_with_the_injected_version()
    {
        var console = new TestConsole();
        console.Width(120);

        AppHeader.Render(console, "0.0.0-test");

        console.Output.Replace("\r\n", "\n").Should().Be(
            "DotHelper 0.0.0-test — asistente para .NET\n");
    }

    [Fact]
    public void The_default_version_is_the_assembly_version()
    {
        string expected = typeof(AppHeader).Assembly.GetName().Version!.ToString(3);

        AppHeader.DefaultVersion.Should().Be(expected);
        AppHeader.Lines()[0].Should().Contain($"DotHelper[/] [grey]{expected}[/]");
    }

    [Fact]
    public void The_banner_is_one_compact_line_so_it_fits_dialogs()
    {
        AppHeader.Lines().Should().ContainSingle(
            "prompts show the same banner as menus, so it cannot be a tall block");
    }
}