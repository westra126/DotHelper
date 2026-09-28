using DotHelper.Core.Dotnet;

using FluentAssertions;

namespace DotHelper.Tests.Unit;

public sealed class DotnetRunnerTests
{
    [Fact]
    public async Task DryRun_does_not_execute_and_returns_exact_command_line()
    {
        using TempLogDir logDir = new();
        var runner = new DotnetRunner(new DotnetRunnerOptions { DryRun = true, LogDirectory = logDir.Path });

        DotnetResult result = await runner.RunAsync(
            new[] { "new", "list", "--ignore-constraints", "--columns-all" },
            workingDir: null,
            cancellationToken: CancellationToken.None);

        result.DryRun.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.StdOut.Should().BeEmpty();
        result.StdErr.Should().BeEmpty();
        result.CommandLine.Should().Be("dotnet new list --ignore-constraints --columns-all");
    }

    [Fact]
    public async Task DryRun_quotes_arguments_that_contain_spaces()
    {
        using TempLogDir logDir = new();
        var runner = new DotnetRunner(new DotnetRunnerOptions { DryRun = true, LogDirectory = logDir.Path });

        DotnetResult result = await runner.RunAsync(
            new[] { "new", "console", "-o", "My App" },
            workingDir: null,
            cancellationToken: CancellationToken.None);

        result.CommandLine.Should().Be("dotnet new console -o \"My App\"");
    }

    [Fact]
    public void Default_log_directory_follows_the_plan()
    {
        // Pure computation (PLAN.md §6a): no file is written to the user home in tests.
        string dir = DotnetRunner.DefaultLogDirectory();

        dir.Should().EndWith(Path.Combine(".local", "state", "dothelper", "logs"));
    }

    [Fact]
    public async Task Real_execution_of_dotnet_version_succeeds()
    {
        using TempLogDir logDir = new();
        var runner = new DotnetRunner(new DotnetRunnerOptions { LogDirectory = logDir.Path });

        DotnetResult result = await runner.RunAsync(
            new[] { "--version" },
            workingDir: null,
            cancellationToken: CancellationToken.None);

        result.DryRun.Should().BeFalse();
        result.ExitCode.Should().Be(0);
        result.StdOut.Should().NotBeNullOrWhiteSpace();
        result.StdErr.Should().BeEmpty();
        result.CommandLine.Should().Be("dotnet --version");
    }

    [Fact]
    public async Task Always_injects_English_cli_ui_language_into_the_child()
    {
        // Parent pretends to be Spanish; the child must still get English (PLAN.md §6a).
        string? previous = Environment.GetEnvironmentVariable("DOTNET_CLI_UI_LANGUAGE");
        Environment.SetEnvironmentVariable("DOTNET_CLI_UI_LANGUAGE", "es");
        try
        {
            using TempLogDir logDir = new();
            var runner = new DotnetRunner(new DotnetRunnerOptions { LogDirectory = logDir.Path });

            DotnetResult result = await runner.RunAsync(
                TemplateCatalog.ListArgs,
                workingDir: null,
                cancellationToken: CancellationToken.None);

            result.ExitCode.Should().Be(0);
            result.StdOut.Should().Contain("Template Name");
            result.StdOut.Should().NotContain("Nombre de la plantilla");
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_CLI_UI_LANGUAGE", previous);
        }
    }

    [Fact]
    public async Task Streams_stdout_lines_via_callback()
    {
        using TempLogDir logDir = new();
        var runner = new DotnetRunner(new DotnetRunnerOptions { LogDirectory = logDir.Path });
        List<string> lines = [];

        await runner.RunAsync(
            new[] { "--version" },
            workingDir: null,
            cancellationToken: CancellationToken.None,
            onStdOutLine: lines.Add);

        lines.Should().NotBeEmpty();
        lines[0].Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Writes_a_log_file_with_the_invoked_command()
    {
        using TempLogDir logDir = new();
        var runner = new DotnetRunner(new DotnetRunnerOptions { LogDirectory = logDir.Path });

        await runner.RunAsync(new[] { "--version" }, workingDir: null, cancellationToken: CancellationToken.None);

        string[] files = Directory.GetFiles(logDir.Path);
        files.Should().NotBeEmpty();
        string content = File.ReadAllText(files[0]);
        content.Should().Contain("dotnet --version");
        content.Should().Contain("code=0");
    }

    [Fact]
    public async Task Log_file_name_uses_the_local_date()
    {
        // Fase 6: the file is named after the local date (an evening session lands in that
        // day's file); timestamps inside stay UTC.
        using TempLogDir logDir = new();
        var runner = new DotnetRunner(new DotnetRunnerOptions { LogDirectory = logDir.Path });

        await runner.RunAsync(new[] { "--version" }, workingDir: null, cancellationToken: CancellationToken.None);

        string[] files = Directory.GetFiles(logDir.Path);
        files.Should().ContainSingle();
        Path.GetFileName(files[0]).Should().Be($"dothelper-{DateTime.Now:yyyyMMdd}.log");
    }

    [Fact]
    public async Task Log_file_is_private_on_unix()
    {
        // Fase 6 review: logs may hold command lines/paths — 0600, not the umask default.
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes do not apply on Windows.");
        }

        if (!OperatingSystem.IsWindows())
        {
            using TempLogDir logDir = new();
            var runner = new DotnetRunner(new DotnetRunnerOptions { LogDirectory = logDir.Path });

            await runner.RunAsync(new[] { "--version" }, workingDir: null, cancellationToken: CancellationToken.None);

            string file = Directory.GetFiles(logDir.Path)[0];
            UnixFileMode mode = File.GetUnixFileMode(file);

            mode.Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public async Task DryRun_writes_a_log_line_without_starting_a_process()
    {
        using TempLogDir logDir = new();
        var runner = new DotnetRunner(new DotnetRunnerOptions { DryRun = true, LogDirectory = logDir.Path });

        await runner.RunAsync(new[] { "--version" }, workingDir: null, cancellationToken: CancellationToken.None);

        string content = File.ReadAllText(Directory.GetFiles(logDir.Path)[0]);
        content.Should().Contain("DRY-RUN dotnet --version");
    }

    private sealed class TempLogDir : IDisposable
    {
        public TempLogDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "dothelper-log-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}