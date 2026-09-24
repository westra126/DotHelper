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
    /// Runs the picker and returns the selected item, or <c>default</c> when cancelled with Esc.
    /// </summary>
    /// <remarks>
    /// Ctrl+C surfaces as <see cref="OperationCanceledException"/> (the caller wires
    /// <see cref="Console.CancelKeyPress"/> to a <see cref="CancellationToken"/>).
    ///
    /// Non-interactive fallback: when <see cref="Console.IsInputRedirected"/> is true and there is
    /// no initial query, this method throws <see cref="InvalidOperationException"/> because no key
    /// can be read — callers must fall back (e.g. print a plain table). When stdin is redirected
    /// but an initial query is present, the top-ranked item is returned without any key I/O.
    /// </remarks>
    public T? Pick(CancellationToken cancellationToken = default)
    {
        string query = _options.InitialQuery ?? string.Empty;

        if (Console.IsInputRedirected)
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

        int selectedIndex = 0;
        int scrollOffset = 0;
        bool showDetail = false;
        T? result = default;
        bool done = false;

        IReadOnlyList<ScoredItem<T>> ranked = RankItems(query);

        bool previousTreatControlCAsInput = Console.TreatControlCAsInput;
        Console.TreatControlCAsInput = true;
        try
        {
            LiveDisplay live = _console
                .Live(BuildView(query, ranked, selectedIndex, scrollOffset, showDetail))
                .AutoClear(true);

            live.Start(ctx =>
            {
                while (!done)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    ConsoleKeyInfo keyInfo = Console.ReadKey(intercept: true);
                    PickerKeyEvent evt = PickerKeyEvent.FromConsoleKeyInfo(keyInfo);

                    switch (evt.Action)
                    {
                        case PickerAction.CancelProcess:
                            throw new OperationCanceledException(cancellationToken);

                        case PickerAction.Cancel:
                            result = default;
                            done = true;
                            break;

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
                        ClampScroll(ranked.Count, ref selectedIndex, ref scrollOffset);
                        ctx.UpdateTarget(BuildView(query, ranked, selectedIndex, scrollOffset, showDetail));
                        ctx.Refresh();
                    }
                }
            });
        }
        finally
        {
            Console.TreatControlCAsInput = previousTreatControlCAsInput;
        }

        return result;
    }

    private IReadOnlyList<ScoredItem<T>> RankItems(string query) =>
        FuzzyScorer.Rank(query, _items, _options.Fields, _options.Cutoff, _options.Tiebreak);

    private static T? Current(IReadOnlyList<ScoredItem<T>> ranked, int selectedIndex) =>
        selectedIndex >= 0 && selectedIndex < ranked.Count ? ranked[selectedIndex].Item : default;

    /// <summary>Keeps the selected row inside the visible window (pure, testable).</summary>
    internal static void ClampScroll(int count, ref int selectedIndex, ref int scrollOffset, int pageSize = 12)
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
        bool showDetail)
    {
        List<IRenderable> elements = [];

        string title = _options.Title ?? "select";
        elements.Add(new Markup(
            $"{Theme.PromptSymbol} [{Theme.AccentMarkup}]{Markup.Escape(title)}[/] {Markup.Escape(query)}█"));

        if (ranked.Count == 0)
        {
            elements.Add(new Markup($"[{Theme.MutedMarkup}](no matches)[/]"));
        }
        else
        {
            int end = Math.Min(scrollOffset + _options.PageSize, ranked.Count);
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

            if (ranked.Count > _options.PageSize)
            {
                elements.Add(new Markup(
                    $"[{Theme.MutedMarkup}]{selectedIndex + 1}/{ranked.Count}[/]"));
            }
        }

        if (showDetail && ranked.Count > 0 && selectedIndex < ranked.Count)
        {
            elements.Add(new Rule($"[{Theme.MutedMarkup}]detail[/]").RuleStyle(Style.Plain));
            IReadOnlyList<string> detail = _options.DetailLines?.Invoke(ranked[selectedIndex].Item) ?? [];
            foreach (string line in detail)
            {
                elements.Add(new Markup($"[{Theme.MutedMarkup}]{Markup.Escape(line)}[/]"));
            }
        }

        elements.Add(new Markup(
            $"[{Theme.MutedMarkup}]↑/↓ move · Enter select · Esc cancel · Tab detail[/]"));

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