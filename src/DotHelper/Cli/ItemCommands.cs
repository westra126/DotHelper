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
    public static async Task<int> RunAsync(ItemCommandSettings settings, CancellationToken cancellationToken)
    {
        IAnsiConsole console = AnsiConsole.Console;
        IDotnetRunner discovery = CliSupport.CreateDiscoveryRunner(settings);
        IDotnetRunner mutating = CliSupport.CreateMutatingRunner(settings);

        var catalog = new TemplateCatalog(discovery);
        IReadOnlyList<TemplateInfo> all = await catalog.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<TemplateInfo> itemTemplates = all
            .Where(static t => t.Type.Equals("item", StringComparison.OrdinalIgnoreCase))
            .ToList();

        TemplateInfo? template = ResolveTemplate(console, itemTemplates, settings);
        if (template is null)
        {
            CliSupport.PrintError("No item template selected.");
            return 1;
        }

        string templateShortName = template.ShortNames.FirstOrDefault() ?? template.Name;
        string name = CliSupport.RequireValue(console, settings.Name, "Name:", "NewFile", settings.Yes);
        if (CliSupport.RejectFlagLike(name, "Name"))
        {
            return 1;
        }

        WorkspaceContext workspace = await CliSupport
            .ResolveWorkspaceAsync(discovery, cancellationToken)
            .ConfigureAwait(false);

        string? projectFile = ResolveProject(console, workspace, settings);
        if (projectFile is null)
        {
            CliSupport.PrintError("No target project selected.");
            return 1;
        }

        string projectDirectory = Path.GetDirectoryName(Path.GetFullPath(projectFile))
            ?? Environment.CurrentDirectory;

        string outputSubdir = CliSupport.RequireValue(
            console,
            settings.Output,
            "Folder (Enter = project root):",
            string.Empty,
            settings.Yes,
            allowEmpty: true);

        var itemService = new ItemService(mutating);
        DotnetResult result = await itemService
            .CreateAsync(templateShortName, name, projectDirectory, outputSubdir, projectFile, cancellationToken)
            .ConfigureAwait(false);

        if (CliSupport.IsFailed(result))
        {
            return CliSupport.FailMutation(result);
        }

        string targetDirectory = string.IsNullOrWhiteSpace(outputSubdir)
            ? projectDirectory
            : Path.Combine(projectDirectory, outputSubdir);
        string? createdPath = result.DryRun
            ? Path.Combine(targetDirectory, name)
            : CreatedFileResolver.FindByBaseName(targetDirectory, name);
        if (createdPath is null)
        {
            CliSupport.PrintError($"Could not find the created file for '{name}' under {targetDirectory}.");
            return 1;
        }

        return CliSupport.FinishMutation(settings, result, $"Created {createdPath}", $"would create {createdPath}");
    }

    private static TemplateInfo? ResolveTemplate(
        IAnsiConsole console,
        IReadOnlyList<TemplateInfo> itemTemplates,
        ItemCommandSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Template))
        {
            TemplateInfo? byShortName = itemTemplates.FirstOrDefault(t =>
                t.ShortNames.Contains(settings.Template!, StringComparer.OrdinalIgnoreCase));
            return byShortName ?? itemTemplates.FirstOrDefault(t =>
                t.Name.Equals(settings.Template!, StringComparison.OrdinalIgnoreCase));
        }

        return CliSupport.ChooseTemplate(console, itemTemplates, settings.Query, settings.Yes);
    }

    private static string? ResolveProject(
        IAnsiConsole console,
        WorkspaceContext workspace,
        ItemCommandSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Project))
        {
            string full = Path.GetFullPath(settings.Project!);
            return File.Exists(full) ? full : null;
        }

        if (workspace.SolutionProjectPaths.Count > 0)
        {
            return CliSupport.ChooseProject(
                console, workspace.SolutionProjectPaths, "project", settings.Query, settings.Yes);
        }

        return workspace.ClosestProjectPath;
    }

}