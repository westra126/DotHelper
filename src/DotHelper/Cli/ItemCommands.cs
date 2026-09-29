using DotHelper.Core.Dotnet;
using DotHelper.Core.Workspace;
using DotHelper.Ui;

using Spectre.Console;
using Spectre.Console.Cli;

namespace DotHelper.Cli;

/// <summary>
/// <c>dh item [template] [--name] [--project] [--output] [--query]</c> — PLAN.md §5.2 / §5.3.
/// </summary>
public sealed class ItemCommandSettings : WorkspaceCommandSettings
{
    [CommandArgument(0, "[template]")]
    public string? Template { get; init; }

    [CommandOption("-n|--name <NAME>")]
    public string? Name { get; init; }

    [CommandOption("--project <PROJ>")]
    public string? Project { get; init; }

    /// <summary>Folder relative to the project directory (empty = project root).</summary>
    [CommandOption("--output <DIR>")]
    public string? Output { get; init; }

    [CommandOption("--query <QUERY>")]
    public string? Query { get; init; }
}

/// <summary>Creates a new item (class, record, interface…) inside a project.</summary>
public sealed class ItemCommand : AsyncCommand<ItemCommandSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        ItemCommandSettings settings,
        CancellationToken cancellationToken)
    {
        return await ItemFlow.RunAsync(settings, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// <c>dh class</c> — alias of <c>dh item</c> that pre-loads the query <c>class</c>
/// (PLAN.md §5.2). Delegates to <see cref="ItemFlow"/>; zero duplicated logic.
/// </summary>
public sealed class ClassCommand : AsyncCommand<ItemCommandSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        ItemCommandSettings settings,
        CancellationToken cancellationToken)
    {
        ItemCommandSettings preset = PresetFor(settings);

        return await ItemFlow.RunAsync(preset, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the <c>dh item</c> settings preset with the <c>class</c> query (internal for
    /// tests). <see cref="WorkspaceCommandSettings.InheritFrom"/> copies every common flag so
    /// <c>--print-cmd</c>/<c>--dry-run</c>/<c>--verbose</c> are never lost.
    /// </summary>
    internal static ItemCommandSettings PresetFor(ItemCommandSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        ItemCommandSettings preset = new()
        {
            Template = settings.Template,
            Name = settings.Name,
            Project = settings.Project,
            Output = settings.Output,
            Query = string.IsNullOrWhiteSpace(settings.Query) ? "class" : settings.Query,
        };
        preset.InheritFrom(settings);
        return preset;
    }
}

/// <summary>Shared item-creation flow (PLAN.md §5.3).</summary>
public static class ItemFlow
{
    /// <summary>
    /// Runs the flow inside one fullscreen session (user reports 1–3): the template picker, the
    /// Name/Folder prompts and the project picker all live in the same alternate screen, and the
    /// result messages are flushed on the restored primary screen. Esc rewinds step by step
    /// (Folder → Project → Name → Template) keeping previous answers as defaults/preselections;
    /// Esc at the first interactive step exits the flow (see <see cref="FlowNavigator"/> and
    /// <paramref name="firstStepEsc"/>).
    /// </summary>
    public static Task<int> RunAsync(
        ItemCommandSettings settings,
        CancellationToken cancellationToken,
        EscHint firstStepEsc = EscHint.Cancel)
    {
        IAnsiConsole console = AnsiConsole.Console;
        return ScreenSession.RunAsync(console, () => RunCoreAsync(console, settings, cancellationToken, firstStepEsc));
    }

    private static async Task<int> RunCoreAsync(
        IAnsiConsole console,
        ItemCommandSettings settings,
        CancellationToken cancellationToken,
        EscHint firstStepEsc)
    {
        IDotnetRunner discovery = CliSupport.CreateDiscoveryRunner(settings);
        IDotnetRunner mutating = CliSupport.CreateMutatingRunner(settings);

        var catalog = new TemplateCatalog(discovery);
        IReadOnlyList<TemplateInfo> all = await catalog.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<TemplateInfo> itemTemplates = all
            .Where(static t => t.Type.Equals("item", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Answers kept across rewinds ("keep what was chosen"): re-asked steps offer them as
        // defaults (prompts) / preselection (pickers).
        TemplateInfo? template = null;
        string? name = null;
        WorkspaceContext? workspace = null;
        string? projectFile = null;
        string? outputSubdir = null;

        FlowNavigator nav = new(firstStepEsc);
        while (true)
        {
            try
            {
                if (nav.Step > 3)
                {
                    break;
                }

                switch (nav.Step)
                {
                    case 0: // template
                        if (!string.IsNullOrWhiteSpace(settings.Template))
                        {
                            // Provided on the command line: auto-resolved, never a step.
                            template = FindTemplate(itemTemplates, settings.Template!);
                        }
                        else
                        {
                            template = CliSupport.ChooseTemplate(
                                console, itemTemplates, settings.Query, settings.Yes, nav, template);
                        }

                        if (template is null)
                        {
                            CliSupport.PrintError("No item template selected.");
                            return 1;
                        }

                        nav.Next();
                        break;

                    case 1: // name
                        name = CliSupport.RequireValue(
                            console, settings.Name, "Name:", name ?? "NewFile", settings.Yes, nav: nav);
                        if (CliSupport.RejectFlagLike(name, "Name"))
                        {
                            return 1;
                        }

                        nav.Next();
                        break;

                    case 2: // project
                        if (!string.IsNullOrWhiteSpace(settings.Project))
                        {
                            string full = Path.GetFullPath(settings.Project!);
                            projectFile = File.Exists(full) ? full : null;
                        }
                        else
                        {
                            workspace ??= await CliSupport
                                .ResolveWorkspaceAsync(discovery, cancellationToken)
                                .ConfigureAwait(false);

                            projectFile = workspace.SolutionProjectPaths.Count > 0
                                ? CliSupport.ChooseProject(
                                    console,
                                    workspace.SolutionProjectPaths,
                                    "project",
                                    settings.Query,
                                    settings.Yes,
                                    nav,
                                    projectFile)
                                : workspace.ClosestProjectPath;
                        }

                        if (projectFile is null)
                        {
                            CliSupport.PrintError("No target project selected.");
                            return 1;
                        }

                        nav.Next();
                        break;

                    case 3: // folder
                        outputSubdir = CliSupport.RequireValue(
                            console,
                            settings.Output,
                            "Folder (Enter = project root):",
                            outputSubdir ?? string.Empty,
                            settings.Yes,
                            allowEmpty: true,
                            nav);
                        nav.Next();
                        break;
                }
            }
            catch (PromptCancelledException)
            {
                // Esc: rewind one visible step, or exit the flow at its first one.
                if (!nav.TryRewind())
                {
                    throw;
                }
            }
        }

        string templateShortName = template!.ShortNames.FirstOrDefault() ?? template.Name;
        string projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectFile!))
            ?? Environment.CurrentDirectory;
        string folder = outputSubdir ?? string.Empty;

        var itemService = new ItemService(mutating);
        DotnetResult result = await itemService
            .CreateAsync(templateShortName, name!, projectDirectory, folder, projectFile!, cancellationToken)
            .ConfigureAwait(false);

        if (CliSupport.IsFailed(result))
        {
            return CliSupport.FailMutation(result);
        }

        string targetDirectory = string.IsNullOrWhiteSpace(folder)
            ? projectDirectory
            : Path.Combine(projectDirectory, folder);
        string? createdPath = result.DryRun
            ? Path.Combine(targetDirectory, name!)
            : CreatedFileResolver.FindByBaseName(targetDirectory, name!);
        if (createdPath is null)
        {
            CliSupport.PrintError($"Could not find the created file for '{name}' under {targetDirectory}.");
            return 1;
        }

        return CliSupport.FinishMutation(settings, result, $"Created {createdPath}", $"would create {createdPath}");
    }

    private static TemplateInfo? FindTemplate(IReadOnlyList<TemplateInfo> itemTemplates, string provided)
    {
        TemplateInfo? byShortName = itemTemplates.FirstOrDefault(t =>
            t.ShortNames.Contains(provided, StringComparer.OrdinalIgnoreCase));
        return byShortName ?? itemTemplates.FirstOrDefault(t =>
            t.Name.Equals(provided, StringComparison.OrdinalIgnoreCase));
    }
}