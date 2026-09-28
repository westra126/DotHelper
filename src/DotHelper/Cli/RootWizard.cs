using DotHelper.Ui;

using Spectre.Console;
using Spectre.Console.Cli;

namespace DotHelper.Cli;

/// <summary>
/// <c>dh</c> (no arguments) — root interactive wizard (PLAN.md §5.2 / §5.3).
/// Inherits the common workspace flags so <c>dh --dry-run</c>/<c>--print-cmd</c>/<c>--verbose</c>
/// reach the dispatched flows (PLAN.md §5.2 lists <c>--dry-run</c> as a global flag).
/// </summary>
public sealed class RootWizardSettings : WorkspaceCommandSettings
{
    /// <summary>Pre-fills the action filter (e.g. <c>dh --query "cl"</c>).</summary>
    [CommandOption("-q|--query <QUERY>")]
    public string? Query { get; init; }
}

/// <summary>
/// Entry point of the root wizard. Registered as the default command (Fase 5): invoking
/// <c>dh</c> without arguments runs the wizard instead of the help screen.
/// </summary>
public sealed class RootWizardCommand : AsyncCommand<RootWizardSettings>
{
    /// <summary>
    /// Help renderer used when there is no TTY (set by the entry point to
    /// <c>app.Run(new[] { "--help" })</c>). Overridable for tests.
    /// </summary>
    public static Func<int>? HelpFallback { get; set; }

    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        RootWizardSettings settings,
        CancellationToken cancellationToken)
    {
        var wizard = new RootWizard(
            AnsiConsole.Console,
            keyReader: null,
            executor: (item, ct) => WizardDispatcher.RunAsync(item, settings, ct),
            showHelp: HelpFallback,
            inputRedirected: null,
            version: null);

        // Cancellations and flow errors reach the global handler (CommandErrors).
        return await wizard.RunAsync(settings.Query, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Root interactive wizard: a fuzzy menu of the six PLAN.md §5.2 actions (plus the NuGet /
/// Referencias / Listar sub-menus) that dispatches to the existing command flows — no logic is
/// duplicated. Key reading is injectable (<see cref="IKeyReader"/>) so tests can drive the menu
/// without a TTY.
/// </summary>
public sealed class RootWizard
{
    private readonly IAnsiConsole _console;
    private readonly IKeyReader? _keyReader;
    private readonly Func<WizardItem, CancellationToken, Task<int>> _executor;
    private readonly Func<int> _showHelp;
    private readonly bool _inputRedirected;
    private readonly string _version;

    /// <param name="console">Console to render to (a <c>TestConsole</c> in tests).</param>
    /// <param name="keyReader">Key source; <c>null</c> reads the real console.</param>
    /// <param name="executor">Runs the selected action; defaults to <see cref="WizardDispatcher"/>.</param>
    /// <param name="showHelp">Fallback used when there is no TTY; defaults to a usage line.</param>
    /// <param name="inputRedirected">Overrides <see cref="Console.IsInputRedirected"/> (tests).</param>
    /// <param name="version">Version shown in the header; defaults to the assembly version.</param>
    public RootWizard(
        IAnsiConsole console,
        IKeyReader? keyReader = null,
        Func<WizardItem, CancellationToken, Task<int>>? executor = null,
        Func<int>? showHelp = null,
        bool? inputRedirected = null,
        string? version = null)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _keyReader = keyReader;
        _executor = executor ?? WizardDispatcher.RunAsync;
        _showHelp = showHelp ?? DefaultShowHelp;
        _inputRedirected = inputRedirected ?? Console.IsInputRedirected;
        _version = version ?? typeof(RootWizard).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    /// <summary>
    /// Runs the wizard: header, action picker, optional sub-menu picker, then the action flow.
    /// Esc at any menu prints <c>Cancelled.</c> and exits 0; Ctrl+C surfaces as
    /// <see cref="OperationCanceledException"/> (exit 130 at the command layer).
    /// </summary>
    public async Task<int> RunAsync(string? query, CancellationToken cancellationToken)
    {
        // No TTY and no injected keys → never call ReadKey: keep the help as the fallback.
        if (_keyReader is null && _inputRedirected)
        {
            return _showHelp();
        }

        RenderHeader(_console, _version);

        WizardItem? action = Pick(WizardMenu.Root, "action", query);
        if (action is null)
        {
            _console.WriteLine("Cancelled.");
            return 0;
        }

        if (action.Children.Count > 0)
        {
            action = Pick(action.Children, action.Title, query: null);
            if (action is null)
            {
                _console.WriteLine("Cancelled.");
                return 0;
            }
        }

        return await _executor(action, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Header with the product title, version and key hints (PLAN.md §5.3). Separated from the
    /// menu so tests can snapshot this stable block on its own.
    /// </summary>
    internal static void RenderHeader(IAnsiConsole console, string version)
    {
        ArgumentNullException.ThrowIfNull(console);

        console.MarkupLine(
            $"[{Theme.AccentMarkup}]DotHelper[/] [{Theme.MutedMarkup}]{Markup.Escape(version)}[/] " +
            $"[{Theme.MutedMarkup}]— asistente para .NET[/]");
        console.MarkupLine(
            $"[{Theme.MutedMarkup}]↑/↓ mover · Enter seleccionar · Esc cancelar · Tab detalle · escribir para filtrar[/]");
    }

    private WizardItem? Pick(IReadOnlyList<WizardItem> items, string title, string? query)
    {
        var picker = new FuzzyPicker<WizardItem>(
            _console,
            items,
            new FuzzyPickerOptions<WizardItem>
            {
                PrimaryText = static i => i.Title,
                Fields = WizardMenu.Fields,
                DetailLines = WizardMenu.Detail,
                Title = title,
                InitialQuery = query,
                KeyReader = _keyReader,
            });

        return picker.Pick();
    }

    private static int DefaultShowHelp()
    {
        AnsiConsole.WriteLine("DotHelper — interactive .NET helper. Run 'dh --help' for the command list.");
        return 0;
    }
}