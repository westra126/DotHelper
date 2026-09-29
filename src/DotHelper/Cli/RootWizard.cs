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
    /// Runs the wizard: action picker (with the banner header inside its frame), optional
    /// sub-menu picker, then the action flow — all inside ONE fullscreen session
    /// (<see cref="ScreenSession"/>), so the menu never flashes the terminal history and the
    /// result messages land on the restored primary screen. Esc navigates back (user report):
    /// sub-menu → root menu, root menu → exit 0, and a flow's first interactive step → the menu
    /// the flow was dispatched from (caught <see cref="PromptCancelledException"/>). Ctrl+C
    /// surfaces as <see cref="OperationCanceledException"/> (exit 130 at the command layer).
    /// </summary>
    public async Task<int> RunAsync(string? query, CancellationToken cancellationToken)
    {
        // No TTY and no injected keys → never call ReadKey: keep the help as the fallback.
        if (_keyReader is null && _inputRedirected)
        {
            return _showHelp();
        }

        return await ScreenSession
            .RunAsync(_console, () => RunInteractiveAsync(query, cancellationToken))
            .ConfigureAwait(false);
    }

    private async Task<int> RunInteractiveAsync(string? query, CancellationToken cancellationToken)
    {
        IReadOnlyList<WizardItem> menu = WizardMenu.Root;
        string title = "action";
        string? seed = query;
        bool inSubMenu = false;

        while (true)
        {
            WizardItem? action = Pick(menu, title, seed, inSubMenu ? EscHint.Back : EscHint.Exit);
            seed = null; // the query seeds only the very first frame

            if (action is null)
            {
                if (inSubMenu)
                {
                    // Esc on a sub-menu goes back to the root menu (no exit).
                    inSubMenu = false;
                    menu = WizardMenu.Root;
                    title = "action";
                    continue;
                }

                OutputChannel.WriteLine(_console, "Cancelled.");
                return 0;
            }

            if (action.Children.Count > 0)
            {
                inSubMenu = true;
                menu = action.Children;
                title = action.Title;
                continue;
            }

            try
            {
                return await _executor(action, cancellationToken).ConfigureAwait(false);
            }
            catch (PromptCancelledException)
            {
                // Esc at the flow's first interactive step: back to the menu it came from
                // (sub-menu when the leaf was picked there, root menu otherwise).
            }
        }
    }

    /// <summary>
    /// Header lines of the wizard banner: the canonical <see cref="AppHeader"/> lines with the
    /// wizard's (test-injectable) version. Rendered inside the picker frame
    /// (<see cref="FuzzyPickerOptions{T}.Header"/>) while the fullscreen session is open.
    /// </summary>
    internal static IReadOnlyList<string> HeaderLines(string version) => AppHeader.Lines(version);

    /// <summary>
    /// Prints the banner lines directly (kept as the canonical, byte-stable rendering of
    /// <see cref="HeaderLines"/> — the interactive path embeds them in the picker instead).
    /// </summary>
    internal static void RenderHeader(IAnsiConsole console, string version)
    {
        ArgumentNullException.ThrowIfNull(console);

        AppHeader.Render(console, version);
    }

    private WizardItem? Pick(IReadOnlyList<WizardItem> items, string title, string? query, EscHint escHint)
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
                Header = HeaderLines(_version),
                EscHint = escHint,
                InitialQuery = query,
                KeyReader = _keyReader,
            });

        try
        {
            return picker.Pick();
        }
        catch (PromptCancelledException)
        {
            // Menu Esc: the loop decides — back to the parent menu, or exit at the root.
            return null;
        }
    }

    private static int DefaultShowHelp()
    {
        OutputChannel.WriteLine(
            AnsiConsole.Console,
            "DotHelper — interactive .NET helper. Run 'dh --help' for the command list.");
        return 0;
    }
}