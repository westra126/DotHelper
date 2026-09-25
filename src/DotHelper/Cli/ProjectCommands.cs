using DotHelper.Core.Dotnet;
using DotHelper.Core.Workspace;
using DotHelper.Ui;

using Spectre.Console;
using Spectre.Console.Cli;

namespace DotHelper.Cli;

/// <summary><c>dh project list</c> — projects of the active solution (or closest csproj).</summary>
public sealed class ProjectListSettings : WorkspaceCommandSettings
{
}

/// <summary><c>dh project add-ref [--from] [--to] [--query]</c>.</summary>
public class ProjectAddRefSettings : WorkspaceCommandSettings
{
    [CommandOption("--from <PROJ>")]
    public string? From { get; init; }

    [CommandOption("--to <PROJ>")]
    public string? To { get; init; }

    [CommandOption("--query <QUERY>")]
    public string? Query { get; init; }
}

/// <summary><c>dh project remove-ref [--from] [--to] [--query]</c>.</summary>
public sealed class ProjectRemoveRefSettings : ProjectAddRefSettings
{
}

/// <summary>Lists projects of the active workspace.</summary>
public sealed class ProjectListCommand : AsyncCommand<ProjectListSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        ProjectListSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ProjectListFlow.RunAsync(settings, listOnly: true, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.WriteLine("Cancelled.");
            return 130;
        }
    }
}

/// <summary>Adds a project reference (origin → destination) with double picker.</summary>
public sealed class ProjectAddRefCommand : AsyncCommand<ProjectAddRefSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        ProjectAddRefSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ProjectRefFlow.RunAsync(settings, remove: false, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.WriteLine("Cancelled.");
            return 130;
        }
    }
}

/// <summary>Removes a project reference (origin → destination) with double picker.</summary>
public sealed class ProjectRemoveRefCommand : AsyncCommand<ProjectRemoveRefSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        ProjectRemoveRefSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ProjectRefFlow.RunAsync(settings, remove: true, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.WriteLine("Cancelled.");
            return 130;
        }
    }
}

/// <summary>Shared list flow for <c>dh project list</c> and <c>dh sln list</c>.</summary>
public static class ProjectListFlow
{
    public static async Task<int> RunAsync(
        WorkspaceCommandSettings settings,
        bool listOnly,
        CancellationToken cancellationToken)
    {
        IAnsiConsole console = AnsiConsole.Console;
        IDotnetRunner discovery = CliSupport.CreateDiscoveryRunner(settings);

        WorkspaceContext workspace = await CliSupport
            .ResolveWorkspaceAsync(discovery, cancellationToken)
            .ConfigureAwait(false);

        if (workspace.SolutionPath is not null && workspace.SolutionProjectPaths.Count > 0)
        {
            var table = new Table();
            table.AddColumn("Project");
            table.AddColumn("In");
            string slnName = Path.GetFileName(workspace.SolutionPath);
            foreach (string project in workspace.SolutionProjectPaths)
            {
                table.AddRow(Markup.Escape(project), Markup.Escape(slnName));
            }

            AnsiConsole.Write(table);
            return 0;
        }

        if (workspace.ClosestProjectPath is not null)
        {
            AnsiConsole.MarkupLine($"[{Theme.MutedMarkup}]No solution — closest project:[/]");
            AnsiConsole.MarkupLine(Markup.Escape(workspace.ClosestProjectPath));
            return 0;
        }

        CliSupport.PrintError("No solution or project found.");
        return 1;
    }
}

/// <summary>Shared add-ref / remove-ref flow with double picker (PLAN.md §5.2).</summary>
public static class ProjectRefFlow
{
    public static async Task<int> RunAsync(
        ProjectAddRefSettings settings,
        bool remove,
        CancellationToken cancellationToken)
    {
        IAnsiConsole console = AnsiConsole.Console;
        IDotnetRunner discovery = CliSupport.CreateDiscoveryRunner(settings);
        IDotnetRunner mutating = CliSupport.CreateMutatingRunner(settings);

        WorkspaceContext workspace = await CliSupport
            .ResolveWorkspaceAsync(discovery, cancellationToken)
            .ConfigureAwait(false);

        if (workspace.SolutionProjectPaths.Count < 2)
        {
            CliSupport.PrintError("Need at least two projects in the active workspace to wire a reference.");
            return 1;
        }

        IReadOnlyList<string> projects = workspace.SolutionProjectPaths;

        string? from = ResolveSide(console, settings.From, projects, "source project", settings.Query, settings.Yes);
        if (from is null)
        {
            CliSupport.PrintError("No source project selected.");
            return 1;
        }

        IReadOnlyList<string> targets = projects.Where(p => !string.Equals(p, from, StringComparison.Ordinal)).ToList();
        string? to = ResolveSide(console, settings.To, targets, "target project", settings.Query, settings.Yes);
        if (to is null)
        {
            CliSupport.PrintError("No target project selected.");
            return 1;
        }

        var projectService = new ProjectService(mutating);
        DotnetResult result = remove
            ? await projectService.RemoveReferenceAsync(from, to, cancellationToken).ConfigureAwait(false)
            : await projectService.AddReferenceAsync(from, to, cancellationToken).ConfigureAwait(false);

        string verb = remove ? "Removed" : "Added";
        CliSupport.PrintSuccess($"{verb} reference {Path.GetFileName(from)} → {Path.GetFileName(to)}");
        CliSupport.PrintCommand(result);
        return 0;
    }

    private static string? ResolveSide(
        IAnsiConsole console,
        string? provided,
        IReadOnlyList<string> projects,
        string title,
        string? query,
        bool yes)
    {
        if (!string.IsNullOrWhiteSpace(provided))
        {
            string full = Path.GetFullPath(provided!);
            if (File.Exists(full))
            {
                return full;
            }

            // Allow selecting by file name (e.g. "Core.csproj") or by name fragment.
            string? byName = projects.FirstOrDefault(p =>
                Path.GetFileName(p).Equals(provided, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileNameWithoutExtension(p).Equals(provided, StringComparison.OrdinalIgnoreCase));
            return byName;
        }

        return CliSupport.ChooseProject(console, projects, title, query, yes);
    }
}