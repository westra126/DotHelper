using DotHelper.Core.Dotnet;

using FluentAssertions;

using NSubstitute;

namespace DotHelper.Tests.Unit;

public sealed class TemplateCatalogTests
{
    [Fact]
    public async Task GetTemplatesAsync_parses_runner_output()
    {
        string fixture = File.ReadAllText(Fixtures.Resolve("new-list-en.txt"));
        IDotnetRunner runner = StubRunner(fixture);
        var catalog = new TemplateCatalog(runner);

        IReadOnlyList<TemplateInfo> templates = await catalog.GetTemplatesAsync(CancellationToken.None);

        templates.Should().NotBeEmpty();
        templates.Should().Contain(static t => t.ShortNames.Contains("classlib"));
    }

    [Fact]
    public async Task GetTemplatesAsync_caches_within_ttl()
    {
        IDotnetRunner runner = StubRunner(string.Empty);
        var catalog = new TemplateCatalog(runner, ttl: TimeSpan.FromHours(1));

        await catalog.GetTemplatesAsync(CancellationToken.None);
        await catalog.GetTemplatesAsync(CancellationToken.None);

        await runner.Received(1).RunAsync(
            Arg.Any<IEnumerable<string>>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<Action<string>?>(),
            Arg.Any<Action<string>?>());
    }

    [Fact]
    public async Task GetTemplatesAsync_refreshes_when_ttl_is_zero()
    {
        IDotnetRunner runner = StubRunner(string.Empty);
        var catalog = new TemplateCatalog(runner, ttl: TimeSpan.Zero);

        await catalog.GetTemplatesAsync(CancellationToken.None);
        await catalog.GetTemplatesAsync(CancellationToken.None);

        await runner.Received(2).RunAsync(
            Arg.Any<IEnumerable<string>>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<Action<string>?>(),
            Arg.Any<Action<string>?>());
    }

    [Fact]
    public async Task GetTemplatesAsync_returns_empty_for_output_without_separator()
    {
        IDotnetRunner runner = StubRunner("no table here");
        var catalog = new TemplateCatalog(runner);

        IReadOnlyList<TemplateInfo> templates = await catalog.GetTemplatesAsync(CancellationToken.None);

        templates.Should().BeEmpty();
    }

    private static IDotnetRunner StubRunner(string stdout)
    {
        IDotnetRunner runner = Substitute.For<IDotnetRunner>();
        runner.RunAsync(
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<Action<string>?>())
            .Returns(new DotnetResult
            {
                ExitCode = 0,
                StdOut = stdout,
                StdErr = string.Empty,
                CommandLine = "dotnet new list --ignore-constraints --columns-all",
                DryRun = false,
            });
        return runner;
    }
}