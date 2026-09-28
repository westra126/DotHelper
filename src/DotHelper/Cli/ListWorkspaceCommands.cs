using DotHelper.Core.Workspace;

using Spectre.Console;
using Spectre.Console.Cli;

namespace DotHelper.Cli;

/// <summary><c>dh list solutions</c> — <c>*.sln</c>/<c>*.slnx</c> under the current directory.</summary>
public sealed class ListSolutionsSettings : WorkspaceCommandSettings
{
}

/// <summary><c>dh list projects</c> — projects of the active solution (or closest csproj).</summary>
public sealed class ListProjectsSettings : WorkspaceCommandSettings
{
}

/// <summary>
/// Flow for <c>dh list solutions</c> (shared with the root wizard).
/// </summary>
public static class ListSolutionsFlow
{
    /// <summary>Lists solution files found under the current directory.</summary>
    public static int Run()
    {
        IReadOnlyList<string> solutions = WorkspaceScanner.FindSolutions(Environment.CurrentDirectory);

        if (solutions.Count == 0)
        {
            CliSupport.PrintError("No .sln/.slnx files found under the current directory.");
            return 1;
        }

        var table = new Table();
        table.AddColumn("Solution");
        foreach (string solution in solutions)
        {
            table.AddRow(Markup.Escape(solution));
        }

        AnsiConsole.Write(table);
        return 0;
    }
}

/// <summary>Lists solution files found under the current directory.</summary>
public sealed class ListSolutionsCommand : AsyncCommand<ListSolutionsSettings>
{
    protected override Task<int> ExecuteAsync(
        CommandContext context,
        ListSolutionsSettings settings,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(ListSolutionsFlow.Run());
    }
}

/// <summary>Lists projects of the active solution (or the closest csproj).</summary>
public sealed class ListProjectsCommand : AsyncCommand<ListProjectsSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        ListProjectsSettings settings,
        CancellationToken cancellationToken)
    {
        return await ProjectListFlow.RunAsync(settings, cancellationToken).ConfigureAwait(false);
    }
}