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
    /// candidate, otherwise an interactive <see cref="FuzzyPicker{T}"/> is used. When the picker
    /// is really shown the step is recorded in <paramref name="nav"/> (so Esc can rewind to it)
    /// and <paramref name="initialSelection"/> preselects the remembered choice; Esc raises
    /// <see cref="PromptCancelledException"/> (the flow's back signal).
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
        bool yes,
        FlowNavigator? nav = null,
        T? initialSelection = default)
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

        // The dialog is really about to ask: record the step and derive its Esc hint.
        EscHint escHint = nav?.Ask() ?? EscHint.Cancel;

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
                InitialSelection = initialSelection,
                EscHint = escHint,
            });

        return picker.Pick();
    }

    /// <summary>Template picker over a pre-filtered catalog slice.</summary>
    public static TemplateInfo? ChooseTemplate(
        IAnsiConsole console,
        IReadOnlyList<TemplateInfo> candidates,
        string? query,
        bool yes,
        FlowNavigator? nav = null,
        TemplateInfo? initialSelection = null) =>
        Choose(
            console,
            "templates",
            candidates,
            static t => t.Name,
            TemplateSearch.Fields,
            TemplateSearch.PickerOptions().DetailLines,
            TemplateSearch.TypeTiebreak,
            query,
            yes,
            nav,
            initialSelection);

    /// <summary>Project picker over absolute project paths.</summary>
    public static string? ChooseProject(
        IAnsiConsole console,
        IReadOnlyList<string> projectPaths,
        string title,
        string? query,
        bool yes,
        FlowNavigator? nav = null,
        string? initialSelection = null) =>
        Choose(
            console,
            title,
            projectPaths,
            static p => p,
            static p => new[] { new WeightedField(p, WeightedField.ShortNameWeight) },
            static p => new[] { p },
            tiebreak: null,
            query,
            yes,
            nav,
            initialSelection);

    /// <summary>
    /// Returns a provided value or prompts/default under <c>--yes</c>. When the prompt is really
    /// shown the step is recorded in <paramref name="nav"/> (Esc rewinds to it) and its Esc hint
    /// is derived from the flow position.
    /// </summary>
    public static string RequireValue(
        IAnsiConsole console,
        string? provided,
        string promptText,
        string defaultValue,
        bool yes,
        bool allowEmpty = false,
        FlowNavigator? nav = null)
    {
        if (!string.IsNullOrWhiteSpace(provided))
        {
            return provided;
        }

        if (yes)
        {
            return defaultValue;
        }

        EscHint escHint = nav?.Ask() ?? EscHint.Cancel;
        return allowEmpty
            ? Prompts.AskFolder(
                console, promptText, defaultValue.Length > 0 ? defaultValue : null, escHint: escHint)
            : Prompts.AskName(
                console, promptText, defaultValue.Length > 0 ? defaultValue : null, escHint: escHint);
    }

    // Result messages go through OutputChannel: while a ScreenSession owns the alternate
    // screen they are deferred and flushed on the restored primary screen (user reports 1–3).

    public static void PrintSuccess(string message) =>
        OutputChannel.MarkupLine(AnsiConsole.Console, $"[green]✔[/] {Markup.Escape(message)}");

    public static void PrintCommand(DotnetResult result) =>
        OutputChannel.MarkupLine(AnsiConsole.Console, $"  [grey]({Markup.Escape(result.CommandLine)})[/]");

    public static void PrintError(string message) =>
        OutputChannel.MarkupLine(AnsiConsole.Console, $"[red]Error:[/] {Markup.Escape(message)}");

    /// <summary>
    /// Outcome line of a mutating command (pure): in dry-run the message must be unmistakable
    /// (<c>Dry-run: would …</c>) instead of claiming a past success (Fase 6 polish).
    /// </summary>
    public static string FormatOutcome(DotnetResult result, string successMessage, string wouldMessage)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(successMessage);
        ArgumentException.ThrowIfNullOrWhiteSpace(wouldMessage);

        return result.DryRun ? $"Dry-run: {wouldMessage}" : successMessage;
    }

    /// <summary>
    /// Prints the outcome of a mutation followed by the equivalent <c>dotnet</c> command:
    /// an unmistakable <c>Dry-run: would …</c> line when nothing ran, a success line otherwise.
    /// </summary>
    public static void PrintOutcome(DotnetResult result, string successMessage, string wouldMessage)
    {
        string message = FormatOutcome(result, successMessage, wouldMessage);
        if (result.DryRun)
        {
            OutputChannel.MarkupLine(
                AnsiConsole.Console, $"[{Theme.MutedMarkup}]○[/] [grey]{Markup.Escape(message)}[/]");
        }
        else
        {
            PrintSuccess(message);
        }

        PrintCommand(result);
    }

    /// <summary>
    /// True when a mutating <paramref name="result"/> failed: a real (non dry-run) run with a
    /// non-zero exit code. Pure decision logic shared by every mutating flow.
    /// </summary>
    public static bool IsFailed(DotnetResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return !result.DryRun && result.ExitCode != 0;
    }

    /// <summary>Reports a failed mutation: friendly error line, no stack trace, exit code 1.</summary>
    public static int FailMutation(DotnetResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        PrintError(NugetService.DescribeError(result));
        return 1;
    }

    /// <summary>
    /// Uniform end of a mutating flow: reports the failure (exit 1) when the <c>dotnet</c> run
    /// failed, otherwise prints the outcome line and copies every equivalent command under
    /// <c>--print-cmd</c> (PLAN.md §5.3 / §6e). <paramref name="extraCommandLines"/> are
    /// commands of the same flow run earlier (e.g. the project create before add-to-sln).
    /// </summary>
    public static int FinishMutation(
        WorkspaceCommandSettings settings,
        DotnetResult result,
        string successMessage,
        string wouldMessage,
        params string[] extraCommandLines)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(result);

        if (IsFailed(result))
        {
            return FailMutation(result);
        }

        PrintOutcome(result, successMessage, wouldMessage);
        string[] all = [.. extraCommandLines, result.CommandLine];
        CopyCommands(settings, all);
        return 0;
    }

    /// <summary>
    /// True when a user-supplied value starts with <c>-</c> and could therefore be
    /// mis-parsed as a flag by <c>dotnet</c> when forwarded positionally (names, package
    /// ids, search terms). Paths are exempt: legitimate paths may start with a dash.
    /// </summary>
    public static bool LooksLikeFlag(string? value) =>
        !string.IsNullOrEmpty(value) && value![0] == '-';

    /// <summary>
    /// Rejects user values that look like flags (leading <c>-</c>) before they reach
    /// <c>dotnet</c>; returns false and prints a clear message otherwise.
    /// </summary>
    public static bool RejectFlagLike(string? value, string fieldName)
    {
        if (!LooksLikeFlag(value))
        {
            return false;
        }

        PrintError($"{fieldName} must not start with '-': {value}");
        return true;
    }

    /// <summary>
    /// <c>--print-cmd</c> (PLAN.md §6e): copies the equivalent <c>dotnet</c> command(s) to the
    /// clipboard through <see cref="Clipboard"/> when available. Best-effort — a missing or broken
    /// clipboard tool never breaks the flow; only <c>--verbose</c> surfaces the reason.
    /// </summary>
    public static void CopyCommands(WorkspaceCommandSettings settings, params string[] commandLines)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(commandLines);

        if (!settings.PrintCmd || commandLines.Length == 0)
        {
            return;
        }

        ClipboardResult result = Clipboard.TryCopy(string.Join(Environment.NewLine, commandLines));
        switch (result.Status)
        {
            case ClipboardStatus.Copied:
                OutputChannel.MarkupLine(
                    AnsiConsole.Console,
                    $"[{Theme.MutedMarkup}]Clipboard:[/] copied {commandLines.Length} command(s) via {Markup.Escape(result.Tool ?? "?")}");
                break;

            case ClipboardStatus.NoTool when settings.Verbose:
                OutputChannel.MarkupLine(
                    AnsiConsole.Console,
                    $"[{Theme.MutedMarkup}]Clipboard:[/] no tool found ({string.Join("/", Clipboard.CandidateTools)}); nothing copied.");
                break;

            case ClipboardStatus.Failed when settings.Verbose:
                OutputChannel.MarkupLine(
                    AnsiConsole.Console,
                    $"[{Theme.MutedMarkup}]Clipboard:[/] {Markup.Escape(result.Tool ?? "?")} failed " +
                    $"({Markup.Escape(result.Error ?? "unknown")}); nothing copied.");
                break;
        }
    }

    /// <summary>Combines name + output the way <c>dh new project</c> does: output is the parent folder.</summary>
    public static string ComposeProjectDirectory(string? outputParent, string name) =>
        string.IsNullOrWhiteSpace(outputParent) ? name : Path.Combine(outputParent, name);
}