using DotHelper.Core.Dotnet;

using FluentAssertions;

using NSubstitute;

namespace DotHelper.Tests.Unit;

public sealed class NugetServiceTests
{
    // ---- BuildSearchArgs ----

    [Fact]
    public void Search_args_use_json_format_and_detailed_verbosity()
    {
        IReadOnlyList<string> args = NugetService.BuildSearchArgs("Newtonsoft", 5, prerelease: false);

        args.Should().Equal("package", "search", "Newtonsoft", "--take", "5", "--format", "json", "--verbosity", "detailed");
    }

    [Fact]
    public void Search_args_omit_the_term_when_empty_and_add_prerelease_when_asked()
    {
        IReadOnlyList<string> args = NugetService.BuildSearchArgs("  ", 20, prerelease: true);

        args.Should().Equal("package", "search", "--take", "20", "--format", "json", "--verbosity", "detailed", "--prerelease");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Search_args_reject_non_positive_take(int take)
    {
        FluentActions.Invoking(() => NugetService.BuildSearchArgs("x", take, prerelease: false))
            .Should()
            .Throw<ArgumentOutOfRangeException>();
    }

    // ---- BuildListArgs / BuildAddArgs / BuildRemoveArgs ----

    [Fact]
    public void List_args_place_the_project_before_the_package_verb()
    {
        NugetService.BuildListArgs("src/App.csproj", includeTransitive: false)
            .Should()
            .Equal("list", "src/App.csproj", "package");

        NugetService.BuildListArgs("src/App.csproj", includeTransitive: true)
            .Should()
            .Equal("list", "src/App.csproj", "package", "--include-transitive");
    }

    [Fact]
    public void Add_args_omit_the_version_when_not_given()
    {
        NugetService.BuildAddArgs("src/App.csproj", "Newtonsoft.Json", version: null)
            .Should()
            .Equal("add", "src/App.csproj", "package", "Newtonsoft.Json");

        NugetService.BuildAddArgs("src/App.csproj", "Newtonsoft.Json", " 13.0.4 ")
            .Should()
            .Equal("add", "src/App.csproj", "package", "Newtonsoft.Json", "--version", "13.0.4");
    }

    [Fact]
    public void Remove_args_target_the_package_reference()
    {
        NugetService.BuildRemoveArgs("src/App.csproj", "Newtonsoft.Json")
            .Should()
            .Equal("remove", "src/App.csproj", "package", "Newtonsoft.Json");
    }

    [Fact]
    public void Argument_builders_reject_blank_inputs()
    {
        FluentActions.Invoking(() => NugetService.BuildListArgs(" ", false))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => NugetService.BuildAddArgs("p.csproj", "", null))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => NugetService.BuildRemoveArgs("p.csproj", " "))
            .Should().Throw<ArgumentException>();
    }

    // ---- IsMissingSearchSubcommand ----

    [Fact]
    public void Missing_subcommand_is_detected_from_the_cli_wording()
    {
        // Real capture of `dotnet notacommandxyz` on SDK 10.
        DotnetResult result = Result(
            exitCode: 1,
            stdErr: "Could not execute because the specified command or file was not found.\n" +
                    "Possible reasons for this include:");

        NugetService.IsMissingSearchSubcommand(result).Should().BeTrue();
    }

    [Fact]
    public void Other_failures_are_not_reported_as_a_missing_subcommand()
    {
        DotnetResult result = Result(exitCode: 1, stdErr: "error: There are no versions available for the package 'X'.");

        NugetService.IsMissingSearchSubcommand(result).Should().BeFalse();
    }

    [Fact]
    public void Successful_runs_are_never_a_missing_subcommand()
    {
        NugetService.IsMissingSearchSubcommand(Result(exitCode: 0, stdOut: "{}")).Should().BeFalse();
    }

    // ---- DescribeError ----

    [Fact]
    public void Error_lines_win_and_lose_their_prefix()
    {
        // Real capture of `dotnet remove <proj> package <not-installed>` (exit 1).
        DotnetResult result = Result(
            exitCode: 1,
            stdOut: "info : Removing PackageReference for package 'Nope.Package' from project 'Probe.csproj'.\n" +
                    "error: Project '/tmp/Probe.csproj' does not contain any PackageReference 'Nope.Package' to Remove.\n");

        NugetService.DescribeError(result)
            .Should()
            .Be("Project '/tmp/Probe.csproj' does not contain any PackageReference 'Nope.Package' to Remove.");
    }

    [Fact]
    public void Msbuild_error_markers_are_also_understood()
    {
        DotnetResult result = Result(
            exitCode: 1,
            stdOut: "  Determining projects to restore...\n" +
                    "/tmp/R.csproj : error NU1102: Unable to find package Newtonsoft.Json with version (>= 99.99.99)\n");

        NugetService.DescribeError(result)
            .Should()
            .Be("NU1102: Unable to find package Newtonsoft.Json with version (>= 99.99.99)");
    }

    [Fact]
    public void First_informative_line_is_used_when_no_error_marker_exists()
    {
        // Real capture of `dotnet list <missing>.csproj package` (exit 1).
        DotnetResult result = Result(
            exitCode: 1,
            stdErr: "Could not find file or directory '/tmp/Nope.csproj'.\n");

        NugetService.DescribeError(result).Should().Be("Could not find file or directory '/tmp/Nope.csproj'.");
    }

    [Fact]
    public void Noise_lines_are_skipped()
    {
        DotnetResult result = Result(
            exitCode: 1,
            stdOut: "  Determining projects to restore...\n  Restored /tmp/x.csproj (in 1 ms).\n");

        NugetService.DescribeError(result).Should().Be("dotnet exited with code 1");
    }

    // ---- BuildAzureSearchUri ----

    [Fact]
    public void Azure_search_uri_escapes_the_query_and_encodes_flags()
    {
        string uri = NugetService.BuildAzureSearchUri("json & co", 5, prerelease: true);

        uri.Should().StartWith(NugetService.AzureSearchEndpoint + "?q=json%20%26%20co");
        uri.Should().EndWith("&take=5&prerelease=true");
    }

    [Fact]
    public async Task Search_http_timeout_is_reported_as_a_friendly_error()
    {
        IDotnetRunner runner = Substitute.For<IDotnetRunner>();
        runner.RunAsync(
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<Action<string>?>(),
                Arg.Any<Action<string>?>())
            .Returns(Result(exitCode: 1, stdErr: "Could not execute because the specified command or file was not found."));
        using var http = new HttpClient(new TimeoutHandler());
        var service = new NugetService(runner, http);

        Func<Task> act = () => service.SearchAsync("json", take: 3);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*timed out*");
    }

    /// <summary>Simulates the HttpClient timeout: TaskCanceledException without user cancellation.</summary>
    private sealed class TimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new TaskCanceledException("simulated timeout");
        }
    }

    private static DotnetResult Result(int exitCode, string stdOut = "", string stdErr = "") =>
        new()
        {
            ExitCode = exitCode,
            StdOut = stdOut,
            StdErr = stdErr,
            CommandLine = "dotnet package search x",
            DryRun = false,
        };
}