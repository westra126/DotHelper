using DotHelper.Core.Dotnet;
using DotHelper.Core.Workspace;

using Spectre.Console;
using Spectre.Console.Cli;

namespace DotHelper.Cli;

/// <summary><c>dh sln list</c> — projects of the active solution.</summary>
public sealed class SlnListSettings : WorkspaceCommandSettings
{
}

/// <summary><c>dh sln add [project]</c> — add a project to the active solution.</summary>
public sealed class SlnAddSettings : WorkspaceCommandSettings
{
    [CommandArgument(0, "[project]")]
    public string? Project { get; init; }

    [CommandOption("--query <QUERY>")]
    public string? Query { get; init; }
}

/// <summary><c>dh sln remove [project]</c> — remove a project from the active solution.</summary>
public sealed class SlnRemoveSettings : WorkspaceCommandSettings
{
    [CommandArgument(0, "[project]")]
    public string? Project { get; init; }

    [CommandOption("--query <QUERY>")]
    public string? Query { get; init; }
}

/// <summary>Lists projects of the active solution.</summary>
public sealed class SlnListCommand : AsyncCommand<SlnListSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        SlnListSettings settings,
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

/// <summary>Adds a project (picker over candidates under the solution root) to the solution.</summary>
public sealed class SlnAddCommand : AsyncCommand<SlnAddSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        SlnAddSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            IAnsiConsole console = AnsiConsole.Console;
            IDotnetRunner discovery = CliSupport.CreateDiscoveryRunner(settings);
            IDotnetRunner mutating = CliSupport.CreateMutatingRunner(settings);

            WorkspaceContext workspace = await CliSupport
                .ResolveWorkspaceAsync(discovery, cancellationToken)
                .ConfigureAwait(false);

            if (workspace.SolutionPath is null)
            {
                CliSupport.PrintError("No solution found (looking for .sln/.slnx upwards).");
                return 1;
            }

            string slnDir = Path.GetDirectoryName(workspace.SolutionPath)!;
            var solutionService = new SolutionService(discovery);
            IReadOnlyList<string> inSln = await solutionService
                .ListProjectsAsync(workspace.SolutionPath, cancellationToken)
                .ConfigureAwait(false);

            HashSet<string> already = new(inSln.Select(p => Path.GetFullPath(Path.Combine(slnDir, p))), StringComparer.Ordinal);

            IReadOnlyList<string> candidates = WorkspaceScanner.FindProjects(slnDir)
                .Where(p => !already.Contains(p))
                .ToList();

            string? projectPath = ResolveProject(console, settings.Project, candidates, settings.Query, settings.Yes);
            if (projectPath is null)
            {
                CliSupport.PrintError(candidates.Count == 0
                    ? "No candidate projects found under the solution root."
                    : "No project selected.");
                return 1;
            }

            var mutatingSolution = new SolutionService(mutating);
            DotnetResult result = await mutatingSolution
                .AddProjectAsync(workspace.SolutionPath, projectPath, cancellationToken)
                .ConfigureAwait(false);

            CliSupport.PrintSuccess($"Added {projectPath} to {Path.GetFileName(workspace.SolutionPath)}");
            CliSupport.PrintCommand(result);
            return 0;
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.WriteLine("Cancelled.");
            return 130;
        }
    }

    internal static string? ResolveProject(
        IAnsiConsole console,
        string? provided,
        IReadOnlyList<string> candidates,
        string? query,
        bool yes)
    {
        if (!string.IsNullOrWhiteSpace(provided))
        {
            string full = Path.GetFullPath(provided!);
            return File.Exists(full) ? full : null;
        }

        return CliSupport.ChooseProject(console, candidates, "project", query, yes);
    }
}

/// <summary>Removes a project (picker over solution projects) from the solution.</summary>
public sealed class SlnRemoveCommand : AsyncCommand<SlnRemoveSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        SlnRemoveSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            IAnsiConsole console = AnsiConsole.Console;
            IDotnetRunner discovery = CliSupport.CreateDiscoveryRunner(settings);
            IDotnetRunner mutating = CliSupport.CreateMutatingRunner(settings);

            WorkspaceContext workspace = await CliSupport
                .ResolveWorkspaceAsync(discovery, cancellationToken)
                .ConfigureAwait(false);

            if (workspace.SolutionPath is null)
            {
                CliSupport.PrintError("No solution found (looking for .sln/.slnx upwards).");
                return 1;
            }

            string? projectPath = SlnAddCommand.ResolveProject(
                console, settings.Project, workspace.SolutionProjectPaths, settings.Query, settings.Yes);

            if (projectPath is null)
            {
                CliSupport.PrintError("No project selected.");
                return 1;
            }

            var solutionService = new SolutionService(mutating);
            DotnetResult result = await solutionService
                .RemoveProjectAsync(workspace.SolutionPath, projectPath, cancellationToken)
                .ConfigureAwait(false);

            CliSupport.PrintSuccess($"Removed {projectPath} from {Path.GetFileName(workspace.SolutionPath)}");
            CliSupport.PrintCommand(result);
            return 0;
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.WriteLine("Cancelled.");
            return 130;
        }
    }
}