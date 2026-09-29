using DotHelper.Core.Dotnet;
using DotHelper.Core.Workspace;
using DotHelper.Ui;

using Spectre.Console;
using Spectre.Console.Cli;

namespace DotHelper.Cli;

/// <summary><c>dh new solution [name] [--format sln|slnx]</c> — PLAN.md §5.2.</summary>
public sealed class NewSolutionSettings : WorkspaceCommandSettings
{
    [CommandArgument(0, "[name]")]
    public string? Name { get; init; }

    [CommandOption("--format <FORMAT>")]
    public string? Format { get; init; }
}

/// <summary><c>dh new project [template] [--name] [--output] [--query] [--add-to-sln]</c> — PLAN.md §5.2.</summary>
public sealed class NewProjectSettings : WorkspaceCommandSettings
{
    [CommandArgument(0, "[template]")]
    public string? Template { get; init; }

    [CommandOption("--name <NAME>")]
    public string? Name { get; init; }

    /// <summary>Parent folder of the new project (project dir becomes <c>&lt;output&gt;/&lt;name&gt;</c>).</summary>
    [CommandOption("--output <DIR>")]
    public string? Output { get; init; }

    [CommandOption("--query <QUERY>")]
    public string? Query { get; init; }

    /// <summary>Force adding the new project to the active solution.</summary>
    [CommandOption("--add-to-sln")]
    public bool AddToSln { get; init; }

    /// <summary>Disable the automatic add-to-solution when a solution is detected.</summary>
    [CommandOption("--no-add-to-sln")]
    public bool NoAddToSln { get; init; }
}

/// <summary>Creates a new solution file.</summary>
public sealed class NewSolutionCommand : AsyncCommand<NewSolutionSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        NewSolutionSettings settings,
        CancellationToken cancellationToken)
    {
        return await NewSolutionFlow.RunAsync(settings, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Creates a new project from a template.</summary>
public sealed class NewProjectCommand : AsyncCommand<NewProjectSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        NewProjectSettings settings,
        CancellationToken cancellationToken)
    {
        return await NewProjectFlow.RunAsync(settings, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Flow shared by <c>dh new project</c> and the "add first project" chain of <c>dh new solution</c>.</summary>
public static class NewSolutionFlow
{
    /// <summary>
    /// Runs the flow inside one fullscreen session (user reports 1–3): the name prompt, the
    /// chained confirmation and the dispatched project flow share a single alternate screen.
    /// Esc rewinds within the ask phase (the name step) keeping the previous answer as the
    /// default; Esc at the first interactive step exits the flow (see <see cref="FlowNavigator"/>
    /// and <paramref name="firstStepEsc"/>). The post-create confirmation is outside the step
    /// machine on purpose: there is no "un-create", so Esc there cancels the remaining chain.
    /// </summary>
    public static Task<int> RunAsync(
        NewSolutionSettings settings,
        CancellationToken cancellationToken,
        EscHint firstStepEsc = EscHint.Cancel)
    {
        IAnsiConsole console = AnsiConsole.Console;
        return ScreenSession.RunAsync(console, () => RunCoreAsync(console, settings, cancellationToken, firstStepEsc));
    }

    private static async Task<int> RunCoreAsync(
        IAnsiConsole console,
        NewSolutionSettings settings,
        CancellationToken cancellationToken,
        EscHint firstStepEsc)
    {
        string? name = null;
        FlowNavigator nav = new(firstStepEsc);
        while (true)
        {
            try
            {
                if (nav.Step > 0)
                {
                    break;
                }

                name = CliSupport.RequireValue(
                    console, settings.Name, "Solution name:", name ?? "App", settings.Yes, nav: nav);
                if (CliSupport.RejectFlagLike(name, "Solution name"))
                {
                    return 1;
                }

                nav.Next();
            }
            catch (PromptCancelledException)
            {
                if (!nav.TryRewind())
                {
                    throw;
                }
            }
        }

        SlnFormat format = ParseFormat(settings.Format);

        IDotnetRunner runner = CliSupport.CreateMutatingRunner(settings);
        var solutionService = new SolutionService(runner);

        DotnetResult result = await solutionService
            .CreateAsync(name!, outputDir: null, format, cancellationToken)
            .ConfigureAwait(false);

        string solutionFile = $"{name}.{(format == SlnFormat.Slnx ? "slnx" : "sln")}";
        int outcome = CliSupport.FinishMutation(
            settings, result, $"Created {solutionFile}", $"would create {solutionFile}");
        if (outcome != 0)
        {
            return outcome;
        }

        if (settings.Yes || settings.DryRun)
        {
            return 0;
        }

        if (Prompts.Confirm(console, "Add the first project now?", defaultValue: true))
        {
            var projectSettings = new NewProjectSettings
            {
                DryRun = settings.DryRun,
                Yes = settings.Yes,
                Verbose = settings.Verbose,
                PrintCmd = settings.PrintCmd,
            };
            return await NewProjectFlow
                .RunAsync(projectSettings, cancellationToken, firstStepEsc)
                .ConfigureAwait(false);
        }

        return 0;
    }

    /// <summary>
    /// Parses <c>--format</c> (internal for tests). Unknown values raise a friendly
    /// <see cref="InvalidOperationException"/> that the command layer maps to exit 1.
    /// </summary>
    internal static SlnFormat ParseFormat(string? format) =>
        format?.Trim().ToLowerInvariant() switch
        {
            null or "" or "sln" => SlnFormat.Sln,
            "slnx" => SlnFormat.Slnx,
            _ => throw new InvalidOperationException(
                $"Unknown solution format '{format}'. Use 'sln' or 'slnx'."),
        };
}

/// <summary>Flow for <c>dh new project</c>.</summary>
public static class NewProjectFlow
{
    /// <summary>
    /// Runs the flow inside one fullscreen session (user reports 1–3): template picker and the
    /// Name/Folder prompts share a single alternate screen; results land on the restored screen.
    /// Esc rewinds step by step (Folder → Name → Template) keeping previous answers; Esc at the
    /// first interactive step exits the flow (see <paramref name="firstStepEsc"/>).
    /// </summary>
    public static Task<int> RunAsync(
        NewProjectSettings settings,
        CancellationToken cancellationToken,
        EscHint firstStepEsc = EscHint.Cancel)
    {
        IAnsiConsole console = AnsiConsole.Console;
        return ScreenSession.RunAsync(console, () => RunCoreAsync(console, settings, cancellationToken, firstStepEsc));
    }

    private static async Task<int> RunCoreAsync(
        IAnsiConsole console,
        NewProjectSettings settings,
        CancellationToken cancellationToken,
        EscHint firstStepEsc)
    {
        IDotnetRunner discovery = CliSupport.CreateDiscoveryRunner(settings);
        IDotnetRunner mutating = CliSupport.CreateMutatingRunner(settings);

        var catalog = new TemplateCatalog(discovery);
        IReadOnlyList<TemplateInfo> all = await catalog.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<TemplateInfo> projectTemplates = all
            .Where(static t => t.Type.Equals("project", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Answers kept across rewinds (see ItemFlow).
        TemplateInfo? template = null;
        string? name = null;
        string? outputParent = null;
        WorkspaceContext? workspace = null;

        FlowNavigator nav = new(firstStepEsc);
        while (true)
        {
            try
            {
                if (nav.Step > 2)
                {
                    break;
                }

                switch (nav.Step)
                {
                    case 0: // template
                        if (!string.IsNullOrWhiteSpace(settings.Template))
                        {
                            template = FindTemplate(projectTemplates, settings.Template!);
                        }
                        else
                        {
                            template = CliSupport.ChooseTemplate(
                                console, projectTemplates, settings.Query, settings.Yes, nav, template);
                        }

                        if (template is null)
                        {
                            CliSupport.PrintError("No project template selected.");
                            return 1;
                        }

                        nav.Next();
                        break;

                    case 1: // name
                        name = CliSupport.RequireValue(
                            console, settings.Name, "Project name:", name ?? "App", settings.Yes, nav: nav);
                        if (CliSupport.RejectFlagLike(name, "Project name"))
                        {
                            return 1;
                        }

                        nav.Next();
                        break;

                    case 2: // folder
                        workspace ??= await CliSupport
                            .ResolveWorkspaceAsync(discovery, cancellationToken)
                            .ConfigureAwait(false);

                        string suggested = FolderSuggester.SuggestProjectFolder(
                            workspace.SolutionPath is not null,
                            template!.ShortNames.FirstOrDefault() ?? template.Name);

                        outputParent = CliSupport.RequireValue(
                            console,
                            settings.Output,
                            "Folder (src/ or tests/):",
                            outputParent ?? suggested,
                            settings.Yes,
                            allowEmpty: true,
                            nav);
                        nav.Next();
                        break;
                }
            }
            catch (PromptCancelledException)
            {
                if (!nav.TryRewind())
                {
                    throw;
                }
            }
        }

        string templateShortName = template!.ShortNames.FirstOrDefault() ?? template.Name;
        string projectDirectory = CliSupport.ComposeProjectDirectory(outputParent, name!);

        var projectService = new ProjectService(mutating);
        DotnetResult create = await projectService
            .CreateAsync(templateShortName, name!, projectDirectory, cancellationToken)
            .ConfigureAwait(false);

        // A failed create must be reported as such: never add-to-sln, never claim success.
        if (CliSupport.IsFailed(create))
        {
            return CliSupport.FailMutation(create);
        }

        // Resolve the created project file instead of assuming ".csproj" (classlib may be
        // created as fsproj/vbproj): deterministic <name>.*proj lookup, clear error otherwise.
        string? projectFile = create.DryRun
            ? Path.Combine(projectDirectory, name + ".csproj")
            : CreatedFileResolver.FindProjectFile(projectDirectory, name!);
        if (projectFile is null)
        {
            CliSupport.PrintError($"Could not find the created project file ({name}.*proj) under {projectDirectory}.");
            return 1;
        }

        CliSupport.PrintOutcome(create, $"Created {projectFile}", $"would create {projectFile}");

        workspace ??= await CliSupport
            .ResolveWorkspaceAsync(discovery, cancellationToken)
            .ConfigureAwait(false);

        bool shouldAdd = settings.AddToSln ||
            (!settings.NoAddToSln && workspace.SolutionPath is not null);

        if (shouldAdd && workspace.SolutionPath is not null)
        {
            var solutionService = new SolutionService(mutating);
            DotnetResult add = await solutionService
                .AddProjectAsync(workspace.SolutionPath, projectFile, cancellationToken)
                .ConfigureAwait(false);
            string slnName = Path.GetFileName(workspace.SolutionPath);
            return CliSupport.FinishMutation(
                settings,
                add,
                $"Added to {slnName}",
                $"would add to {slnName}",
                create.CommandLine);
        }

        if (settings.AddToSln && workspace.SolutionPath is null)
        {
            CliSupport.PrintError("No solution found to add the project to.");
            return 1;
        }

        CliSupport.CopyCommands(settings, create.CommandLine);
        return 0;
    }

    private static TemplateInfo? FindTemplate(IReadOnlyList<TemplateInfo> projectTemplates, string provided)
    {
        TemplateInfo? byShortName = projectTemplates.FirstOrDefault(t =>
            t.ShortNames.Contains(provided, StringComparer.OrdinalIgnoreCase));
        return byShortName ?? projectTemplates.FirstOrDefault(t =>
            t.Name.Equals(provided, StringComparison.OrdinalIgnoreCase));
    }
}