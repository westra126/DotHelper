using DotHelper.Core.Dotnet;
using DotHelper.Core.Workspace;
using DotHelper.Ui;

using Spectre.Console;

namespace DotHelper.Cli;

/// <summary>
/// Resolved location of the "active" workspace (solution + projects) for the current directory.
/// </summary>
public sealed record WorkspaceContext(
    string? SolutionPath,
    IReadOnlyList<string> SolutionProjectPaths,
    string? ClosestProjectPath);

/// <summary>
/// Shared helpers for the workspace commands: runners, discovery, pickers, prompts and the
/// transparency output of the equivalent <c>dotnet</c> commands (PLAN.md §5.3 / §6e).
/// </summary>
public static class CliSupport
{
    /// <summary>Runner used for mutations (honours <c>--dry-run</c>).</summary>
    public static IDotnetRunner CreateMutatingRunner(WorkspaceCommandSettings settings) =>
        new DotnetRunner(new DotnetRunnerOptions
        {
            DryRun = settings.DryRun,
            Verbose = settings.Verbose,
        });

    /// <summary>
    /// Runner used for read-only discovery. Always executes: even in dry-run we may need the
    /// template catalog or <c>dotnet sln list</c> to resolve pickers/defaults. Documented.
    /// </summary>
    public static IDotnetRunner CreateDiscoveryRunner(WorkspaceCommandSettings settings) =>
        new DotnetRunner(new DotnetRunnerOptions { Verbose = settings.Verbose });

    public static async Task<WorkspaceContext> ResolveWorkspaceAsync(
        IDotnetRunner discoveryRunner,
        CancellationToken cancellationToken)
    {
        WorkspaceLookupResult lookup = WorkspaceLocator.Locate(Environment.CurrentDirectory);

        string? solutionPath = lookup.All
            .FirstOrDefault(static a => a.Kind is "sln" or "slnx")?.FullPath;

        string? closestProject = lookup.Closest?.Kind is "csproj" or "fsproj"
            ? lookup.Closest.FullPath
            : lookup.All.FirstOrDefault(static a => a.Kind is "csproj" or "fsproj")?.FullPath;

        List<string> projects = [];
        if (solutionPath is not null)
        {
            string slnDir = Path.GetDirectoryName(solutionPath) ?? Environment.CurrentDirectory;
            var solutionService = new SolutionService(discoveryRunner);
            IReadOnlyList<string> relative = await solutionService
                .ListProjectsAsync(solutionPath, cancellationToken)
                .ConfigureAwait(false);

            foreach (string rel in relative)
            {
                projects.Add(Path.GetFullPath(Path.Combine(slnDir, rel)));
            }
        }
        else if (closestProject is not null)
        {
            projects.Add(closestProject);
        }

        return new WorkspaceContext(solutionPath, projects, closestProject);
    }

    /// <summary>
    /// Picks one item: single candidate short-circuits, <c>--yes</c> takes the top-ranked
    /// candidate, otherwise an interactive <see cref="FuzzyPicker{T}"/> is used.
    /// </summary>
    public static T? Choose<T>(
        IAnsiConsole console,
        string title,
        IReadOnlyList<T> items,
        Func<T, string> primary,
        Func<T, IReadOnlyList<WeightedField>> fields,
        Func<T, IReadOnlyList<string>>? detail,
        IComparer<T>? tiebreak,
        string? query,
        bool yes)
    {
        if (items.Count == 0)
        {
            return default;
        }

        if (items.Count == 1)
        {
            return items[0];
        }

        if (yes)
        {
            IReadOnlyList<ScoredItem<T>> ranked = FuzzyScorer.Rank(query, items, fields, tiebreak: tiebreak);
            return ranked.Count > 0 ? ranked[0].Item : default;
        }

        if (Console.IsInputRedirected && string.IsNullOrWhiteSpace(query))
        {
            throw new InvalidOperationException(
                "Interactive selection is required but stdin is redirected. " +
                "Provide --query/--name/--project to select explicitly, or --yes for non-interactive mode.");
        }

        var picker = new FuzzyPicker<T>(
            console,
            items,
            new FuzzyPickerOptions<T>
            {
                PrimaryText = primary,
                Fields = fields,
                DetailLines = detail,
                Title = title,
                Tiebreak = tiebreak,
                InitialQuery = query,
            });

        return picker.Pick();
    }

    /// <summary>Template picker over a pre-filtered catalog slice.</summary>
    public static TemplateInfo? ChooseTemplate(
        IAnsiConsole console,
        IReadOnlyList<TemplateInfo> candidates,
        string? query,
        bool yes) =>
        Choose(
            console,
            "templates",
            candidates,
            static t => t.Name,
            TemplateSearch.Fields,
            TemplateSearch.PickerOptions().DetailLines,
            TemplateSearch.TypeTiebreak,
            query,
            yes);

    /// <summary>Project picker over absolute project paths.</summary>
    public static string? ChooseProject(
        IAnsiConsole console,
        IReadOnlyList<string> projectPaths,
        string title,
        string? query,
        bool yes) =>
        Choose(
            console,
            title,
            projectPaths,
            static p => p,
            static p => new[] { new WeightedField(p, WeightedField.ShortNameWeight) },
            static p => new[] { p },
            tiebreak: null,
            query,
            yes);

    /// <summary>Returns a provided value or prompts/default under <c>--yes</c>.</summary>
    public static string RequireValue(
        IAnsiConsole console,
        string? provided,
        string promptText,
        string defaultValue,
        bool yes,
        bool allowEmpty = false)
    {
        if (!string.IsNullOrWhiteSpace(provided))
        {
            return provided;
        }

        if (yes)
        {
            return defaultValue;
        }

        return allowEmpty
            ? Prompts.AskFolder(console, promptText, defaultValue.Length > 0 ? defaultValue : null)
            : Prompts.AskName(console, promptText, defaultValue.Length > 0 ? defaultValue : null);
    }

    public static void PrintSuccess(string message) =>
        AnsiConsole.MarkupLine($"[green]✔[/] {Markup.Escape(message)}");

    public static void PrintCommand(DotnetResult result) =>
        AnsiConsole.MarkupLine($"  [grey]({Markup.Escape(result.CommandLine)})[/]");

    public static void PrintError(string message) =>
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");

    /// <summary>Combines name + output the way <c>dh new project</c> does: output is the parent folder.</summary>
    public static string ComposeProjectDirectory(string? outputParent, string name) =>
        string.IsNullOrWhiteSpace(outputParent) ? name : Path.Combine(outputParent, name);
}