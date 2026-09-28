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
/// Flow for <c>dh list templates [--json] [--query &lt;q&gt;]</c>.
/// Interactive fuzzy picker on a TTY, ranked table for <c>--query</c>, full table when stdin
/// is redirected, full JSON catalog for <c>--json</c>. Shared by the command and the root wizard.
/// </summary>
public static class ListTemplatesFlow
{
    private const int QueryResultLimit = 15;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// Runs the flow inside one fullscreen session when it interacts (the picker): the selected
    /// template detail and every message land on the restored primary screen.
    /// </summary>
    public static Task<int> RunAsync(ListTemplatesSettings settings, CancellationToken cancellationToken)
    {
        return ScreenSession.RunAsync(AnsiConsole.Console, () => RunCoreAsync(settings, cancellationToken));
    }

    private static async Task<int> RunCoreAsync(ListTemplatesSettings settings, CancellationToken cancellationToken)
    {
        var runner = new DotnetRunner(new DotnetRunnerOptions { DryRun = settings.DryRun });

        if (settings.DryRun)
        {
            DotnetResult preview = await runner
                .RunAsync(TemplateCatalog.ListArgs, workingDir: null, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            OutputChannel.WriteLine(AnsiConsole.Console, preview.CommandLine);
            return 0;
        }

        var catalog = new TemplateCatalog(runner);
        IReadOnlyList<TemplateInfo> templates = await catalog.GetTemplatesAsync(cancellationToken).ConfigureAwait(false);

        if (settings.Json)
        {
            // Raw JSON contract: never routed through the deferred channel, byte-identical.
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

        // Cancellations reach the global handler (CommandErrors).
        TemplateInfo? selected = picker.Pick(cancellationToken);
        if (selected is null)
        {
            OutputChannel.WriteLine(AnsiConsole.Console, "Cancelled.");
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

        OutputChannel.WriteRenderable(AnsiConsole.Console, table);
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

        OutputChannel.MarkupLine(
            AnsiConsole.Console,
            $"[{Theme.MutedMarkup}]query:[/] {Markup.Escape(query)}  " +
            $"[{Theme.MutedMarkup}]{ranked.Count} match(es), showing {count}[/]");
        OutputChannel.WriteRenderable(AnsiConsole.Console, table);
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

        OutputChannel.WriteRenderable(
            AnsiConsole.Console,
            new Panel(grid).Header("Selected template").BorderColor(Theme.Accent));
    }
}

/// <summary><c>dh list templates</c> — thin wrapper over <see cref="ListTemplatesFlow"/>.</summary>
public sealed class ListTemplatesCommand : AsyncCommand<ListTemplatesSettings>
{
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        ListTemplatesSettings settings,
        CancellationToken cancellationToken)
    {
        return await ListTemplatesFlow.RunAsync(settings, cancellationToken).ConfigureAwait(false);
    }
}