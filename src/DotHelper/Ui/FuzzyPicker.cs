using System.Text;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace DotHelper.Ui;

/// <summary>Configuration of <see cref="FuzzyPicker{T}"/>.</summary>
public sealed class FuzzyPickerOptions<T>
{
    /// <summary>Primary display text of an item (also used for match highlighting).</summary>
    public required Func<T, string> PrimaryText { get; init; }

    /// <summary>Searchable fields with weights (see <see cref="FuzzyScorer"/>).</summary>
    public required Func<T, IReadOnlyList<WeightedField>> Fields { get; init; }

    /// <summary>Secondary rows shown when pressing Tab.</summary>
    public Func<T, IReadOnlyList<string>>? DetailLines { get; init; }

    /// <summary>Short title shown on the input line.</summary>
    public string? Title { get; init; }

    public int PageSize { get; init; } = 12;

    public int Cutoff { get; init; } = FuzzyScorer.DefaultCutoff;

    /// <summary>Optional tiebreak for equal scores (e.g. project before item).</summary>
    public IComparer<T>? Tiebreak { get; init; }

    /// <summary>Optional seed typed into the filter before the first frame.</summary>
    public string? InitialQuery { get; init; }

    /// <summary>
    /// Optional header lines (Spectre markup, trusted) rendered at the very top of the frame.
    /// <c>null</c> (the default) renders the canonical banner (<see cref="AppHeader.Lines"/>) so
    /// every menu/dialog shows it without any call-site wiring; an empty list suppresses it
    /// (tests); a non-empty list overrides it (e.g. a custom version).
    /// </summary>
    public IReadOnlyList<string>? Header { get; init; }

    /// <summary>Contextual Esc hint rendered in the hint row (default: <see cref="EscHint.Cancel"/>).</summary>
    public EscHint EscHint { get; init; } = EscHint.Cancel;

    /// <summary>
    /// Item preselected on the first frame (the remembered choice of a previous step — "keep
    /// what was chosen" semantics of the back navigation). <c>default</c> means no preselection.
    /// </summary>
    public T? InitialSelection { get; init; }

    /// <summary>
    /// Optional key source (Fase 5 testability). When <c>null</c> the picker reads the real
    /// console via <see cref="ConsoleKeyReader"/> exactly as before; when set, the picker is
    /// driven by that reader and the <see cref="Console.IsInputRedirected"/> fallback is skipped.
    /// </summary>
    public IKeyReader? KeyReader { get; init; }
}

/// <summary>
/// Reusable incremental fuzzy picker (PLAN.md §5.1): filter line on top, ranked list below,
/// fzf-style highlight, Enter/Esc/Tab/Arrows/Backspace. Pure logic lives in
/// <see cref="FuzzyScorer"/>, <see cref="FuzzyHighlighter"/> and <see cref="PickerKeyEvent"/>;
/// this type only drives the console.
/// </summary>
/// <typeparam name="T">Item type (template, project, package…).</typeparam>
public sealed class FuzzyPicker<T>
{
    private readonly IAnsiConsole _console;
    private readonly IReadOnlyList<T> _items;
    private readonly FuzzyPickerOptions<T> _options;

    public FuzzyPicker(IAnsiConsole console, IReadOnlyList<T> items, FuzzyPickerOptions<T> options)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Runs the picker and returns the selected item. Esc raises
    /// <see cref="PromptCancelledException"/> — the application-wide "user backed out" signal
    /// (same as prompts), which the surrounding flow's <see cref="FlowNavigator"/> turns into
    /// "go back one step" and the caller of the flow turns into a cancel or a return-to-menu.
    /// The only <c>default</c> result is Enter over an empty match list.
    /// </summary>
    /// <remarks>
    /// Ctrl+C surfaces as <see cref="OperationCanceledException"/> (the caller wires
    /// <see cref="Console.CancelKeyPress"/> to a <see cref="CancellationToken"/>).
    ///
    /// Non-interactive fallback: when reading the real console and <see cref="Console.IsInputRedirected"/>
    /// is true and there is no initial query, this method throws
    /// <see cref="InvalidOperationException"/> because no key can be read — callers must fall back
    /// (e.g. print a plain table). When stdin is redirected but an initial query is present, the
    /// top-ranked item is returned without any key I/O. An injected <see cref="IKeyReader"/> always
    /// drives the interactive loop instead (tests feed a key sequence without a TTY).
    /// </remarks>
    public T? Pick(CancellationToken cancellationToken = default)
    {
        string query = _options.InitialQuery ?? string.Empty;
        IKeyReader? injected = _options.KeyReader;

        if (injected is null && Console.IsInputRedirected)
        {
            if (query.Length == 0)
            {
                throw new InvalidOperationException(
                    "FuzzyPicker requires an interactive TTY. When stdin is redirected, " +
                    "pass an initial query or use a non-interactive fallback instead of ReadKey.");
            }

            IReadOnlyList<ScoredItem<T>> auto = RankItems(query);
            return auto.Count > 0 ? auto[0].Item : default;
        }

        IKeyReader reader = injected ?? ConsoleKeyReader.Instance;
        int selectedIndex = 0;
        int scrollOffset = 0;
        bool showDetail = false;
        T? result = default;
        bool done = false;

        IReadOnlyList<ScoredItem<T>> ranked = RankItems(query);

        // "Keep what was chosen": reopen on the remembered selection when present in the list.
        selectedIndex = IndexOfInitial(ranked);

        // Ctrl+C interception only matters for the real console.
        bool previousTreatControlCAsInput = false;
        if (injected is null)
        {
            previousTreatControlCAsInput = Console.TreatControlCAsInput;
            Console.TreatControlCAsInput = true;
        }

        try
        {
            // Fullscreen session (user reports 1–3): the wizard/command wraps the whole
            // interactive phase in one ScreenSession; the picker only ensures the alternate
            // screen is open before the first frame. Nested pickers/prompts are no-ops and the
            // session restores the primary screen when the outermost scope ends. With an
            // injected key reader (tests) or a capability-less terminal (pipes, TestConsole)
            // nothing is entered — no escape sequences at all.
            ScreenSession.EnsureOpen(_console, injected is not null);
            RunLoop();
        }
        finally
        {
            if (injected is null)
            {
                Console.TreatControlCAsInput = previousTreatControlCAsInput;
            }
        }

        return result;

        // Local function so the alternate-screen action can mutate the loop state.
        void RunLoop()
        {
            PickerLayout layout = EffectiveLayout(showDetail, ranked, selectedIndex, injected);

            LiveDisplay live = _console
                .Live(BuildView(query, ranked, selectedIndex, scrollOffset, showDetail, layout))
                .AutoClear(true);

            live.Start(ctx =>
            {
                // Fix (user report): paint the first frame before reading any key, so the UI is
                // visible immediately instead of waiting for the first keystroke.
                ctx.UpdateTarget(BuildView(query, ranked, selectedIndex, scrollOffset, showDetail, layout));
                ctx.Refresh();

                while (!done)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    ConsoleKeyInfo keyInfo = reader.ReadKey();
                    PickerKeyEvent evt = PickerKeyEvent.FromConsoleKeyInfo(keyInfo);

                    switch (evt.Action)
                    {
                        case PickerAction.CancelProcess:
                            throw new OperationCanceledException(cancellationToken);

                        case PickerAction.Cancel:
                            throw new PromptCancelledException();

                        case PickerAction.Select:
                            result = Current(ranked, selectedIndex);
                            done = true;
                            break;

                        case PickerAction.MoveUp:
                            if (ranked.Count > 0 && selectedIndex > 0)
                            {
                                selectedIndex--;
                            }

                            break;

                        case PickerAction.MoveDown:
                            if (ranked.Count > 0 && selectedIndex < ranked.Count - 1)
                            {
                                selectedIndex++;
                            }

                            break;

                        case PickerAction.ToggleDetail:
                            showDetail = !showDetail;
                            break;

                        case PickerAction.Backspace:
                            if (query.Length > 0)
                            {
                                query = query[..^1];
                                ranked = RankItems(query);
                                selectedIndex = 0;
                                scrollOffset = 0;
                            }

                            break;

                        case PickerAction.Insert:
                            query += evt.Character;
                            ranked = RankItems(query);
                            selectedIndex = 0;
                            scrollOffset = 0;
                            break;
                    }

                    if (!done)
                    {
                        // Recompute the layout each frame: Tab toggles the detail block, which
                        // steals rows from the list so the view never overflows the window.
                        layout = EffectiveLayout(showDetail, ranked, selectedIndex, injected);
                        ClampScroll(ranked.Count, ref selectedIndex, ref scrollOffset, layout.PageSize);
                        ctx.UpdateTarget(BuildView(query, ranked, selectedIndex, scrollOffset, showDetail, layout));
                        ctx.Refresh();
                    }
                }
            });
        }

    }

    private IReadOnlyList<ScoredItem<T>> RankItems(string query) =>
        FuzzyScorer.Rank(query, _items, _options.Fields, _options.Cutoff, _options.Tiebreak);

    /// <summary>Header lines actually rendered: the override, or the canonical banner by default.</summary>
    private IReadOnlyList<string> EffectiveHeader =>
        _options.Header ?? AppHeader.Lines();

    /// <summary>
    /// Index of <see cref="FuzzyPickerOptions{T}.InitialSelection"/> in the ranked list
    /// (0 when absent). Pure helper kept free of rendering.
    /// </summary>
    internal static int IndexOfInitial(IReadOnlyList<ScoredItem<T>> ranked, T? initialSelection)
    {
        if (initialSelection is null)
        {
            return 0;
        }

        for (int i = 0; i < ranked.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(ranked[i].Item, initialSelection))
            {
                return i;
            }
        }

        return 0;
    }

    private int IndexOfInitial(IReadOnlyList<ScoredItem<T>> ranked) =>
        IndexOfInitial(ranked, _options.InitialSelection);

    /// <summary>
    /// Window layout for one frame (Fase: full-height picker). With an injected key reader the
    /// configured <c>PageSize</c> is kept for deterministic tests; with the real console the page
    /// size is derived from the console height so the picker fills the window, shrinking the list
    /// when the Tab detail block is open so nothing overflows.
    /// </summary>
    private PickerLayout EffectiveLayout(bool showDetail, IReadOnlyList<ScoredItem<T>> ranked, int selectedIndex, IKeyReader? injected)
    {
        int detailLines = 0;
        if (showDetail && ranked.Count > 0 && selectedIndex < ranked.Count)
        {
            detailLines = _options.DetailLines?.Invoke(ranked[selectedIndex].Item)?.Count ?? 0;
        }

        return injected is not null
            ? new PickerLayout(_options.PageSize, detailLines)
            : ComputeLayout(
                _console.Profile.Height,
                _options.PageSize,
                showDetail,
                detailLines,
                ranked.Count,
                EffectiveHeader.Count);
    }

    private static T? Current(IReadOnlyList<ScoredItem<T>> ranked, int selectedIndex) =>
        selectedIndex >= 0 && selectedIndex < ranked.Count ? ranked[selectedIndex].Item : default;

    /// <summary>
    /// Pure: sizes the list window so the whole view fits the console. Lines used besides the
    /// list: title+query (1), hints (1), one spare line at the bottom (Live borders) and the
    /// optional header lines; the detail block (rule + lines) and the <c>n/total</c> counter
    /// appear only when present. When the detail block would push the list out of the window,
    /// detail lines are dropped first so at least one list row and the hints/counter stay visible.
    /// </summary>
    /// <param name="consoleHeight">Console height; ≤ 0 means unknown → keep the configured size.</param>
    /// <param name="headerLineCount">Header lines rendered above the input line (see <see cref="FuzzyPickerOptions{T}.Header"/>).</param>
    internal static PickerLayout ComputeLayout(
        int consoleHeight,
        int configuredPageSize,
        bool showDetail,
        int detailLineCount,
        int itemCount,
        int headerLineCount = 0)
    {
        if (consoleHeight <= 0)
        {
            return new PickerLayout(configuredPageSize, showDetail ? Math.Max(0, detailLineCount) : 0);
        }

        int fixedLines = 3 + Math.Max(0, headerLineCount); // title+query, hints, spare line at the bottom
        int wantedDetail = showDetail ? Math.Max(0, detailLineCount) : 0;
        int detailBlock = showDetail ? 1 : 0; // the "detail" rule

        for (int detail = wantedDetail; detail >= 0; detail--)
        {
            int page = consoleHeight - fixedLines - detailBlock - detail;
            if (itemCount > page)
            {
                page -= 1; // the n/total counter needs one line
            }

            if (page >= 1)
            {
                return new PickerLayout(page, showDetail ? detail : 0);
            }
        }

        // Absurdly small window: keep a single row and drop the detail entirely.
        return new PickerLayout(1, 0);
    }

    /// <summary>
    /// Keeps the selected row inside the visible window (pure, testable). The page size is
    /// required on purpose: callers must forward the computed layout, never a default.
    /// </summary>
    internal static void ClampScroll(int count, ref int selectedIndex, ref int scrollOffset, int pageSize)
    {
        if (count <= 0)
        {
            selectedIndex = 0;
            scrollOffset = 0;
            return;
        }

        if (selectedIndex >= count)
        {
            selectedIndex = count - 1;
        }

        if (selectedIndex < 0)
        {
            selectedIndex = 0;
        }

        if (selectedIndex < scrollOffset)
        {
            scrollOffset = selectedIndex;
        }

        if (selectedIndex >= scrollOffset + pageSize)
        {
            scrollOffset = selectedIndex - pageSize + 1;
        }

        if (scrollOffset < 0)
        {
            scrollOffset = 0;
        }
    }

    private IRenderable BuildView(
        string query,
        IReadOnlyList<ScoredItem<T>> ranked,
        int selectedIndex,
        int scrollOffset,
        bool showDetail,
        PickerLayout layout)
    {
        List<IRenderable> elements = [];

        // Header (user report 1 + universal-header report): the canonical banner lives inside
        // the picker frame by default, so it is visible while the alternate screen is open —
        // never on the hidden primary screen. Overridable/suppressible per call site.
        foreach (string line in EffectiveHeader)
        {
            elements.Add(new Markup(line));
        }

        string title = _options.Title ?? "select";
        elements.Add(new Markup(
            $"{Theme.PromptSymbol} [{Theme.AccentMarkup}]{Markup.Escape(title)}[/] {Markup.Escape(query)}█"));

        if (ranked.Count == 0)
        {
            elements.Add(new Markup($"[{Theme.MutedMarkup}](no matches)[/]"));
        }
        else
        {
            int end = Math.Min(scrollOffset + layout.PageSize, ranked.Count);
            for (int i = scrollOffset; i < end; i++)
            {
                T item = ranked[i].Item;
                string primary = _options.PrimaryText(item);
                IReadOnlyList<int> positions = FuzzyHighlighter.FindMatchPositions(primary, query);
                string highlighted = RenderHighlight(primary, positions);

                if (i == selectedIndex)
                {
                    elements.Add(new Markup(
                        $"[{Theme.SelectionMarkup}]{Theme.SelectionSymbol}[/] [{Theme.SelectionMarkup}]{highlighted}[/]"));
                }
                else
                {
                    elements.Add(new Markup($"  {highlighted}"));
                }
            }

            if (ranked.Count > layout.PageSize)
            {
                elements.Add(new Markup(
                    $"[{Theme.MutedMarkup}]{selectedIndex + 1}/{ranked.Count}[/]"));
            }
        }

        if (showDetail && ranked.Count > 0 && selectedIndex < ranked.Count)
        {
            IReadOnlyList<string> detail = _options.DetailLines?.Invoke(ranked[selectedIndex].Item) ?? [];
            if (detail.Count > 0 && layout.DetailLineLimit > 0)
            {
                elements.Add(new Rule($"[{Theme.MutedMarkup}]detail[/]").RuleStyle(Style.Plain));
                int shown = Math.Min(detail.Count, layout.DetailLineLimit);
                for (int i = 0; i < shown; i++)
                {
                    elements.Add(new Markup($"[{Theme.MutedMarkup}]{Markup.Escape(detail[i])}[/]"));
                }
            }
        }

        elements.Add(new Markup(
            $"[{Theme.MutedMarkup}]↑/↓ move · Enter select · {EscHintText.For(_options.EscHint)} · Tab detail · type to filter[/]"));

        return new Rows(elements);
    }

    /// <summary>Renders query matches with the theme highlight. Pure string helper.</summary>
    internal static string RenderHighlight(string text, IReadOnlyList<int> positions)
    {
        if (positions.Count == 0)
        {
            return Markup.Escape(text);
        }

        HashSet<int> marked = new(positions);
        StringBuilder sb = new(text.Length * 2);
        for (int i = 0; i < text.Length; i++)
        {
            string escaped = Markup.Escape(text[i].ToString());
            sb.Append(marked.Contains(i) ? $"[{Theme.HighlightMarkup}]{escaped}[/]" : escaped);
        }

        return sb.ToString();
    }
}

/// <summary>
/// Window layout of one picker frame (Fase: full-height picker): how many list rows fit and how
/// many detail lines may be shown without overflowing the console.
/// </summary>
internal readonly record struct PickerLayout(int PageSize, int DetailLineLimit);