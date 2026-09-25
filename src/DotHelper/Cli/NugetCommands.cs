using System.Text.Json;

using DotHelper.Core.Dotnet;
using DotHelper.Ui;

using Spectre.Console;
using Spectre.Console.Cli;

namespace DotHelper.Cli;

/// <summary><c>dh nuget search [term] [--take 20] [--prerelease] [--json]</c> — PLAN.md §5.2.</summary>
public sealed class NugetSearchSettings : WorkspaceCommandSettings
{
    [CommandArgument(0, "[term]")]
    public string? Term { get; init; }

    /// <summary>Maximum number of results (default 20, matching the CLI).</summary>
    [CommandOption("--take <TAKE>")]
    public int Take { get; init; } = 20;

    [CommandOption("--prerelease")]
    public bool Prerelease { get; init; }

    [CommandOption("--json")]
    public bool Json { get; init; }
}

/// <summary>
/// <c>dh nuget add [package] [--project] [--version] [--query] [--yes] [--dry-run]</c> — PLAN.md §5.2.
/// </summary>
public sealed class NugetAddSettings : WorkspaceCommandSettings
{
    [CommandArgument(0, "[package]")]
    public string? Package { get; init; }

    /// <summary>Target project: full path, file name or name fragment.</summary>
    [CommandOption("--project <PROJ>")]
    public string? Project { get; init; }

    /// <summary>Package version; empty means latest stable.</summary>
    [CommandOption("--version <VERSION>")]
    public string? Version { get; init; }

    /// <summary>Search term used when <c>package</c> is not given (also seeds the picker).</summary>
    [CommandOption("--query <QUERY>")]
    public string? Query { get; init; }
}

/// <summary><c>dh nuget list [--project] [--include-transitive] [--json]</c>.</summary>
public sealed class NugetListSettings : WorkspaceCommandSettings
{
    [CommandOption("--project <PROJ>")]
    public string? Project { get; init; }

    [CommandOption("--include-transitive")]
    public bool IncludeTransitive { get; init; }

    [CommandOption("--json")]
    public bool Json { get; init; }
}

/// <summary><c>dh nuget remove [package] [--project] [--query] [--yes] [--dry-run]</c>.</summary>
public sealed class NugetRemoveSettings : WorkspaceCommandSettings
{
    [CommandArgument(0, "[package]")]
    public string? Package { get; init; }

    [CommandOption("--project <PROJ>")]
    public string? Project { get; init; }

    /// <summary>Fuzzy query over the installed packages when <c>package</c> is not given.</summary>
    [CommandOption("--query <QUERY>")]
    public string? Query { get; init; }
}

/// <summary>Searches nuget.org and prints a table or JSON (never interactive).</summary>
public sealed class NugetSearchCommand : AsyncCommand<NugetSearchSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        NugetSearchSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            return await NugetSearchFlow.RunAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.WriteLine("Cancelled.");
            return 130;
        }
        catch (InvalidOperationException ex)
        {
            CliSupport.PrintError(ex.Message);
            return 1;
        }
    }
}

/// <summary>Adds a package reference with fuzzy search when the id is unknown.</summary>
public sealed class NugetAddCommand : AsyncCommand<NugetAddSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        NugetAddSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            return await NugetAddFlow.RunAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.WriteLine("Cancelled.");
            return 130;
        }
        catch (InvalidOperationException ex)
        {
            CliSupport.PrintError(ex.Message);
            return 1;
        }
    }
}

/// <summary>Lists the package references of a project.</summary>
public sealed class NugetListCommand : AsyncCommand<NugetListSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        NugetListSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            return await NugetListFlow.RunAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.WriteLine("Cancelled.");
            return 130;
        }
        catch (InvalidOperationException ex)
        {
            CliSupport.PrintError(ex.Message);
            return 1;
        }
    }
}

/// <summary>Removes a package reference, with a fuzzy picker over the installed ones.</summary>
public sealed class NugetRemoveCommand : AsyncCommand<NugetRemoveSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        NugetRemoveSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            return await NugetRemoveFlow.RunAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.WriteLine("Cancelled.");
            return 130;
        }
        catch (InvalidOperationException ex)
        {
            CliSupport.PrintError(ex.Message);
            return 1;
        }
    }
}

/// <summary>
/// Shared helpers of the <c>dh nuget</c> branch: project resolution, package pickers and the
/// transparency output of the equivalent <c>dotnet</c> commands (PLAN.md §5.3 / §6e).
/// </summary>
public static class NugetFlow
{
    /// <summary>Results shown when browsing nuget.org from a picker.</summary>
    public const int SearchTake = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>Searchable fields of a search hit: id first, description as a weak signal.</summary>
    public static IReadOnlyList<WeightedField> PackageFields(NugetPackageInfo package)
    {
        ArgumentNullException.ThrowIfNull(package);

        return new[]
        {
            new WeightedField(package.Id, WeightedField.ShortNameWeight),
            new WeightedField(package.Description ?? string.Empty, WeightedField.TagWeight),
        };
    }

    /// <summary>Picker detail of a search hit: latest version, downloads, badge and description.</summary>
    public static IReadOnlyList<string> PackageDetail(NugetPackageInfo package)
    {
        ArgumentNullException.ThrowIfNull(package);

        return new[]
        {
            $"Latest:      {package.LatestVersion}",
            $"Downloads:   {FormatDownloads(package.TotalDownloads)}",
            $"Verified:    {FormatVerified(package.Verified)}",
            $"Description: {package.Description ?? "(none)"}",
        };
    }

    /// <summary>Popular packages first when fuzzy scores tie.</summary>
    public static IComparer<NugetPackageInfo> DownloadsTiebreak { get; } =
        Comparer<NugetPackageInfo>.Create(static (a, b) =>
            (b.TotalDownloads ?? 0).CompareTo(a.TotalDownloads ?? 0));

    /// <summary>Picker detail of an installed package row.</summary>
    public static IReadOnlyList<string> InstalledDetail(InstalledPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        return new[]
        {
            $"Requested: {(package.Requested ?? "(transitive)")}",
            $"Resolved:  {package.Resolved}",
        };
    }

    public static string FormatDownloads(long? totalDownloads) =>
        totalDownloads is null ? "—" : totalDownloads.Value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    public static string FormatVerified(bool? verified) => verified switch
    {
        true => "✓",
        false => "✗",
        null => "?",
    };

    /// <summary>Shortens long descriptions for table cells.</summary>
    public static string Truncate(string? text, int maxLength = 60)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "—";
        }

        string trimmed = text!.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength].TrimEnd() + "…";
    }

    public static string ToJson<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    /// <summary>
    /// Resolves the target project: <paramref name="provided"/> (full path, file name or name
    /// fragment) or the active workspace (solution projects, else the closest project) with a
    /// picker when several candidates exist. Returns <c>null</c> when nothing matches.
    /// </summary>
    public static string? ResolveProject(
        IAnsiConsole console,
        string? provided,
        string? query,
        bool yes,
        WorkspaceContext workspace)
    {
        IReadOnlyList<string> candidates = workspace.SolutionProjectPaths.Count > 0
            ? workspace.SolutionProjectPaths
            : workspace.ClosestProjectPath is null
                ? Array.Empty<string>()
                : new[] { workspace.ClosestProjectPath };

        if (!string.IsNullOrWhiteSpace(provided))
        {
            string full = Path.GetFullPath(provided!);
            if (File.Exists(full))
            {
                return full;
            }

            // Allow selecting by file name (e.g. "Core.csproj") or by name fragment.
            return candidates.FirstOrDefault(p =>
                Path.GetFileName(p).Equals(provided, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileNameWithoutExtension(p).Equals(provided, StringComparison.OrdinalIgnoreCase));
        }

        return CliSupport.ChooseProject(console, candidates, "project", query, yes);
    }

    /// <summary>Prints the exact <c>dotnet</c> command that was (or would be) executed.</summary>
    public static void PrintResult(DotnetResult result, string successMessage, string dryRunMessage)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.DryRun)
        {
            CliSupport.PrintSuccess($"Dry-run: {dryRunMessage}");
        }
        else if (result.ExitCode == 0)
        {
            CliSupport.PrintSuccess(successMessage);
        }

        CliSupport.PrintCommand(result);
    }

    /// <summary>Handles a mutating result: friendly error on failure, transparency line on success.</summary>
    public static int Finish(DotnetResult result, string successMessage, string dryRunMessage)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!result.DryRun && result.ExitCode != 0)
        {
            CliSupport.PrintError(NugetService.DescribeError(result));
            return 1;
        }

        PrintResult(result, successMessage, dryRunMessage);
        return 0;
    }
}

/// <summary>Flow for <c>dh nuget search</c>.</summary>
public static class NugetSearchFlow
{
    public static async Task<int> RunAsync(NugetSearchSettings settings, CancellationToken cancellationToken)
    {
        if (settings.Take < 1)
        {
            CliSupport.PrintError("--take must be greater than zero.");
            return 1;
        }

        if (settings.DryRun)
        {
            var dryRunner = new DotnetRunner(new DotnetRunnerOptions { DryRun = true });
            DotnetResult preview = await dryRunner
                .RunAsync(
                    NugetService.BuildSearchArgs(settings.Term, settings.Take, settings.Prerelease),
                    workingDir: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            AnsiConsole.WriteLine(preview.CommandLine);
            return 0;
        }

        var service = new NugetService(CliSupport.CreateDiscoveryRunner(settings));
        IReadOnlyList<NugetPackageInfo> packages = await service
            .SearchAsync(settings.Term, settings.Take, settings.Prerelease, cancellationToken)
            .ConfigureAwait(false);

        if (settings.Json)
        {
            Console.WriteLine(NugetFlow.ToJson(packages));
            return 0;
        }

        var table = new Table();
        table.AddColumn("Id");
        table.AddColumn("Latest");
        table.AddColumn("Downloads");
        table.AddColumn("Verified");
        table.AddColumn("Description");
        foreach (NugetPackageInfo package in packages)
        {
            table.AddRow(
                Markup.Escape(package.Id),
                Markup.Escape(package.LatestVersion),
                NugetFlow.FormatDownloads(package.TotalDownloads),
                NugetFlow.FormatVerified(package.Verified),
                Markup.Escape(NugetFlow.Truncate(package.Description)));
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

/// <summary>Flow for <c>dh nuget add</c> (PLAN.md §5.3 transparency output).</summary>
public static class NugetAddFlow
{
    public static async Task<int> RunAsync(NugetAddSettings settings, CancellationToken cancellationToken)
    {
        IAnsiConsole console = AnsiConsole.Console;
        IDotnetRunner discovery = CliSupport.CreateDiscoveryRunner(settings);
        IDotnetRunner mutating = CliSupport.CreateMutatingRunner(settings);
        var searchService = new NugetService(discovery);
        var packageService = new NugetService(mutating);

        WorkspaceContext workspace = await CliSupport
            .ResolveWorkspaceAsync(discovery, cancellationToken)
            .ConfigureAwait(false);

        string? projectFile = NugetFlow.ResolveProject(console, settings.Project, query: null, settings.Yes, workspace);
        if (projectFile is null)
        {
            CliSupport.PrintError(string.IsNullOrWhiteSpace(settings.Project)
                ? "No project found (looking for .sln/.csproj upwards)."
                : $"Project not found: {settings.Project}");
            return 1;
        }

        ResolvedPackage? resolved = await ResolvePackageAsync(console, settings, searchService, cancellationToken)
            .ConfigureAwait(false);
        if (resolved is null)
        {
            return 1;
        }

        DotnetResult result = await packageService
            .AddAsync(projectFile, resolved.Id, resolved.Version, cancellationToken)
            .ConfigureAwait(false);

        string target = Path.GetFileName(projectFile);
        return NugetFlow.Finish(
            result,
            $"Added {resolved.Id} to {target}",
            $"would add {resolved.Id} to {target}");
    }

    /// <summary>
    /// Package selection per PLAN.md §5.2: a given <c>package</c> goes straight to
    /// <c>dotnet add</c>; otherwise a search term (from <c>--query</c> or a prompt) feeds the
    /// search and a <see cref="FuzzyPicker{T}"/> with package details. Under <c>--yes</c> the
    /// first search result is used without prompts.
    /// </summary>
    private static async Task<ResolvedPackage?> ResolvePackageAsync(
        IAnsiConsole console,
        NugetAddSettings settings,
        NugetService service,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(settings.Package))
        {
            return new ResolvedPackage(
                settings.Package!.Trim(),
                string.IsNullOrWhiteSpace(settings.Version) ? null : settings.Version.Trim());
        }

        bool interactive = !settings.Yes && !Console.IsInputRedirected;

        string term;
        if (!string.IsNullOrWhiteSpace(settings.Query))
        {
            term = settings.Query!.Trim();
        }
        else if (!interactive)
        {
            CliSupport.PrintError("Provide a package id or --query when running non-interactively.");
            return null;
        }
        else
        {
            term = Prompts.AskName(console, "Search term:");
        }

        IReadOnlyList<NugetPackageInfo> results = await service
            .SearchAsync(term, NugetFlow.SearchTake, prerelease: false, cancellationToken)
            .ConfigureAwait(false);
        if (results.Count == 0)
        {
            CliSupport.PrintError($"No packages found for '{term}'.");
            return null;
        }

        NugetPackageInfo? selected;
        if (settings.Yes)
        {
            // `--yes --query X` → first result without prompts.
            selected = results[0];
        }
        else
        {
            selected = CliSupport.Choose(
                console,
                "packages",
                results,
                static p => p.Id,
                NugetFlow.PackageFields,
                NugetFlow.PackageDetail,
                NugetFlow.DownloadsTiebreak,
                query: null,
                yes: false);
        }

        if (selected is null)
        {
            CliSupport.PrintError("No package selected.");
            return null;
        }

        string? version = settings.Version;
        if (string.IsNullOrWhiteSpace(version) && interactive)
        {
            version = CliSupport.RequireValue(
                console,
                provided: null,
                "Version (Enter = latest stable):",
                string.Empty,
                yes: false,
                allowEmpty: true);
        }

        return new ResolvedPackage(
            selected.Id,
            string.IsNullOrWhiteSpace(version) ? null : version!.Trim());
    }

    private sealed record ResolvedPackage(string Id, string? Version);
}

/// <summary>Flow for <c>dh nuget list</c>.</summary>
public static class NugetListFlow
{
    public static async Task<int> RunAsync(NugetListSettings settings, CancellationToken cancellationToken)
    {
        IAnsiConsole console = AnsiConsole.Console;
        IDotnetRunner discovery = CliSupport.CreateDiscoveryRunner(settings);

        WorkspaceContext workspace = await CliSupport
            .ResolveWorkspaceAsync(discovery, cancellationToken)
            .ConfigureAwait(false);

        string? projectFile = NugetFlow.ResolveProject(console, settings.Project, query: null, settings.Yes, workspace);
        if (projectFile is null)
        {
            CliSupport.PrintError(string.IsNullOrWhiteSpace(settings.Project)
                ? "No project found (looking for .sln/.csproj upwards)."
                : $"Project not found: {settings.Project}");
            return 1;
        }

        if (settings.DryRun)
        {
            var dryRunner = new DotnetRunner(new DotnetRunnerOptions { DryRun = true });
            DotnetResult preview = await dryRunner
                .RunAsync(
                    NugetService.BuildListArgs(projectFile, settings.IncludeTransitive),
                    workingDir: null,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            AnsiConsole.WriteLine(preview.CommandLine);
            return 0;
        }

        var service = new NugetService(discovery);
        IReadOnlyList<InstalledPackage> packages = await service
            .ListAsync(projectFile, settings.IncludeTransitive, cancellationToken)
            .ConfigureAwait(false);

        if (settings.Json)
        {
            Console.WriteLine(NugetFlow.ToJson(packages));
            return 0;
        }

        var table = new Table();
        table.AddColumn("Id");
        table.AddColumn("Requested");
        table.AddColumn("Resolved");
        table.AddColumn("Kind");
        foreach (InstalledPackage package in packages)
        {
            table.AddRow(
                Markup.Escape(package.Id),
                Markup.Escape(package.Requested ?? "—"),
                Markup.Escape(package.Resolved),
                package.IsTransitive ? "transitive" : "top-level");
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

/// <summary>Flow for <c>dh nuget remove</c>.</summary>
public static class NugetRemoveFlow
{
    public static async Task<int> RunAsync(NugetRemoveSettings settings, CancellationToken cancellationToken)
    {
        IAnsiConsole console = AnsiConsole.Console;
        IDotnetRunner discovery = CliSupport.CreateDiscoveryRunner(settings);
        IDotnetRunner mutating = CliSupport.CreateMutatingRunner(settings);

        WorkspaceContext workspace = await CliSupport
            .ResolveWorkspaceAsync(discovery, cancellationToken)
            .ConfigureAwait(false);

        string? projectFile = NugetFlow.ResolveProject(console, settings.Project, query: null, settings.Yes, workspace);
        if (projectFile is null)
        {
            CliSupport.PrintError(string.IsNullOrWhiteSpace(settings.Project)
                ? "No project found (looking for .sln/.csproj upwards)."
                : $"Project not found: {settings.Project}");
            return 1;
        }

        string? packageId = settings.Package?.Trim();
        if (string.IsNullOrWhiteSpace(packageId))
        {
            var searchService = new NugetService(discovery);

            // Only top-level references can be removed, so the picker lists those.
            IReadOnlyList<InstalledPackage> installed = await searchService
                .ListAsync(projectFile, includeTransitive: false, cancellationToken)
                .ConfigureAwait(false);
            if (installed.Count == 0)
            {
                CliSupport.PrintError($"No packages installed in {Path.GetFileName(projectFile)}.");
                return 1;
            }

            bool interactive = !settings.Yes && !Console.IsInputRedirected;
            if (!interactive && string.IsNullOrWhiteSpace(settings.Query) && installed.Count != 1)
            {
                CliSupport.PrintError("Provide a package id or --query when running non-interactively.");
                return 1;
            }

            InstalledPackage? selected = CliSupport.Choose(
                console,
                "packages",
                installed,
                static p => p.Id,
                static p => new[] { new WeightedField(p.Id, WeightedField.ShortNameWeight) },
                NugetFlow.InstalledDetail,
                tiebreak: null,
                settings.Query,
                settings.Yes);

            if (selected is null)
            {
                CliSupport.PrintError(string.IsNullOrWhiteSpace(settings.Query)
                    ? "No package selected."
                    : $"No installed package matches '{settings.Query}'.");
                return 1;
            }

            packageId = selected.Id;
        }

        var packageService = new NugetService(mutating);
        DotnetResult result = await packageService
            .RemoveAsync(projectFile, packageId, cancellationToken)
            .ConfigureAwait(false);

        string target = Path.GetFileName(projectFile);
        return NugetFlow.Finish(
            result,
            $"Removed {packageId} from {target}",
            $"would remove {packageId} from {target}");
    }
}