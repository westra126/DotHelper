using System.Text.Json;

using DotHelper.Core.Dotnet;

using Spectre.Console;
using Spectre.Console.Cli;

namespace DotHelper.Cli;

/// <summary>
/// Settings for <c>dh list templates</c>.
/// <c>--dry-run</c> is wired here as a command option; a true global flag lands in Fase 3/6
/// (PLAN.md §5.2 lists it as a global option).
/// </summary>
public sealed class ListTemplatesSettings : CommandSettings
{
    [CommandOption("--json")]
    public bool Json { get; init; }

    [CommandOption("--dry-run")]
    public bool DryRun { get; init; }
}

/// <summary>
/// <c>dh list templates [--json]</c> — Fase 1 acceptance command.
/// </summary>
public sealed class ListTemplatesCommand : AsyncCommand<ListTemplatesSettings>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        ListTemplatesSettings settings,
        CancellationToken cancellationToken)
    {
        var runner = new DotnetRunner(new DotnetRunnerOptions { DryRun = settings.DryRun });

        if (settings.DryRun)
        {
            DotnetResult preview = await runner
                .RunAsync(TemplateCatalog.ListArgs, workingDir: null, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            AnsiConsole.WriteLine(preview.CommandLine);
            return 0;
        }

        var catalog = new TemplateCatalog(runner);
        IReadOnlyList<TemplateInfo> templates = await catalog.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);

        if (settings.Json)
        {
            Console.WriteLine(JsonSerializer.Serialize(templates, JsonOptions));
            return 0;
        }

        RenderTable(templates);
        return 0;
    }

    private static void RenderTable(IReadOnlyList<TemplateInfo> templates)
    {
        var table = new Table();
        table.AddColumn("Name");
        table.AddColumn("Short Names");
        table.AddColumn("Type");
        table.AddColumn("Languages");

        foreach (TemplateInfo template in templates)
        {
            table.AddRow(
                Markup.Escape(template.Name),
                Markup.Escape(string.Join(", ", template.ShortNames)),
                Markup.Escape(template.Type),
                Markup.Escape(string.Join(", ", template.Languages)));
        }

        AnsiConsole.Write(table);
    }
}