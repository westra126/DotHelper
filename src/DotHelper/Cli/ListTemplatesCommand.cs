using System.Text.Json;

using DotHelper.Core.Dotnet;
using DotHelper.Ui;

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

    /// <summary>Non-interactive fuzzy filter (top 15, ranked, with score).</summary>
    [CommandOption("-q|--query <QUERY>")]
    public string? Query { get; init; }
}

/// <summary>
/// <c>dh list templates [--json] [--query &lt;q&gt;]</c>.
/// Interactive fuzzy picker on a TTY, ranked table for <c>--query</c>, full table when stdin
/// is redirected, full JSON catalog for <c>--json</c>.
/// </summary>
public sealed class ListTemplatesCommand : AsyncCommand<ListTemplatesSettings>
{
    private const int QueryResultLimit = 15;

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

        if (!string.IsNullOrWhiteSpace(settings.Query))
        {
            IReadOnlyList<ScoredItem<TemplateInfo>> ranked = FuzzyScorer.Rank(
                settings.Query,
                templates,
                TemplateSearch.Fields,
                cutoff: FuzzyScorer.DefaultCutoff,
                tiebreak: TemplateSearch.TypeTiebreak);

            RenderRankedTable(ranked, settings.Query);
            return 0;
        }

        // No query: interactive picker on a TTY, plain table otherwise (Fase 1 behavior).
        if (Console.IsInputRedirected)
        {
            RenderTable(templates);
            return 0;
        }

        var picker = new FuzzyPicker<TemplateInfo>(
            AnsiConsole.Console,
            templates,
            TemplateSearch.PickerOptions());

        TemplateInfo? selected;
        try
        {
            selected = picker.Pick(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.WriteLine("Cancelled.");
            return 130;
        }

        if (selected is null)
        {
            AnsiConsole.WriteLine("Cancelled.");
            return 1;
        }

        RenderDetail(selected);
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

    private static void RenderRankedTable(IReadOnlyList<ScoredItem<TemplateInfo>> ranked, string query)
    {
        var table = new Table();
        table.AddColumn("Score");
        table.AddColumn("Name");
        table.AddColumn("Short Names");
        table.AddColumn("Type");
        table.AddColumn("Languages");

        int count = 0;
        foreach (ScoredItem<TemplateInfo> hit in ranked)
        {
            if (count >= QueryResultLimit)
            {
                break;
            }

            table.AddRow(
                hit.Score.ToString(),
                Markup.Escape(hit.Item.Name),
                Markup.Escape(string.Join(", ", hit.Item.ShortNames)),
                Markup.Escape(hit.Item.Type),
                Markup.Escape(string.Join(", ", hit.Item.Languages)));
            count++;
        }

        AnsiConsole.MarkupLine($"[{Theme.MutedMarkup}]query:[/] {Markup.Escape(query)}  " +
            $"[{Theme.MutedMarkup}]{ranked.Count} match(es), showing {count}[/]");
        AnsiConsole.Write(table);
    }

    private static void RenderDetail(TemplateInfo template)
    {
        var grid = new Grid().AddColumn().AddColumn();
        grid.AddRow("Name", Markup.Escape(template.Name));
        grid.AddRow("Short names", Markup.Escape(string.Join(", ", template.ShortNames)));
        grid.AddRow("Type", Markup.Escape(template.Type.Length == 0 ? "(none)" : template.Type));
        grid.AddRow("Languages", Markup.Escape(string.Join(", ", template.Languages)));
        grid.AddRow("Author", Markup.Escape(template.Author));
        grid.AddRow("Tags", Markup.Escape(string.Join(", ", template.Tags)));

        AnsiConsole.Write(new Panel(grid).Header("Selected template").BorderColor(Theme.Accent));
    }
}