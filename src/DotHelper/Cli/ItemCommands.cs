using DotHelper.Core.Dotnet;
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
        try
        {
            return await ItemFlow.RunAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.WriteLine("Cancelled.");
            return 130;
        }
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
        var preset = new ItemCommandSettings
        {
            Template = settings.Template,
            Name = settings.Name,
            Project = settings.Project,
            Output = settings.Output,
            Query = string.IsNullOrWhiteSpace(settings.Query) ? "class" : settings.Query,
            DryRun = settings.DryRun,
            Yes = settings.Yes,
            Verbose = settings.Verbose,
        };

        try
        {
            return await ItemFlow.RunAsync(preset, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.WriteLine("Cancelled.");
            return 130;
        }
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

        string createdPath = ResolveCreatedPath(projectDirectory, outputSubdir, name);
        CliSupport.PrintSuccess($"Created {createdPath}");
        CliSupport.PrintCommand(result);
        return 0;
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

    private static string ResolveCreatedPath(string projectDirectory, string? outputSubdir, string name)
    {
        string directory = string.IsNullOrWhiteSpace(outputSubdir)
            ? projectDirectory
            : Path.Combine(projectDirectory, outputSubdir);

        try
        {
            string[] matches = Directory.Exists(directory)
                ? Directory.GetFiles(directory, name + ".*")
                : [];
            if (matches.Length > 0)
            {
                return matches[0];
            }
        }
        catch (IOException)
        {
            // Fall through to the conventional path.
        }
        catch (UnauthorizedAccessException)
        {
            // Fall through to the conventional path.
        }

        return Path.Combine(directory, name);
    }
}