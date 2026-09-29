using System.Globalization;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace DotHelper.Ui;

/// <summary>
/// Minimal interactive prompts (name, folder, confirm) for the wizards and flows.
///
/// Custom editor on purpose: Spectre.Console 0.55 <c>TextPrompt</c>/<c>ConfirmationPrompt</c>
/// ignore Escape (verified against the upstream <c>ReadLine</c> handler and empirically), so
/// cancelling a text dialog with Esc was impossible. This editor keeps the documented
/// semantics — <see cref="AskName"/> requires a value (defaults when given), <see cref="AskFolder"/>
/// allows empty, <see cref="Confirm"/> is y/n with a default — and adds Esc (→
/// <see cref="PromptCancelledException"/>, the back signal of <see cref="FlowNavigator"/>),
/// Ctrl+C (→ <see cref="OperationCanceledException"/>) and Unicode-safe backspace. Keys come
/// from <see cref="IKeyReader"/> like <see cref="FuzzyPicker{T}"/>; the line repaints per
/// keystroke through <see cref="LiveDisplay"/> so echo/backspace stay correct for any text
/// element. The canonical banner (<see cref="AppHeader"/>) renders at the top of every prompt
/// by default; the Esc hint is contextual (<see cref="EscHintText"/>).
/// </summary>
public static class Prompts
{
    /// <summary>Asks for a non-empty name (project, class, solution…).</summary>
    /// <param name="header"><c>null</c> renders the canonical banner, empty suppresses it, non-empty overrides it.</param>
    /// <exception cref="PromptCancelledException">Esc was pressed.</exception>
    /// <exception cref="OperationCanceledException">Ctrl+C was pressed.</exception>
    public static string AskName(
        IAnsiConsole console,
        string prompt,
        string? defaultValue = null,
        IKeyReader? keyReader = null,
        IReadOnlyList<string>? header = null,
        EscHint escHint = EscHint.Cancel)
    {
        ArgumentNullException.ThrowIfNull(console);

        return ReadText(console, prompt, defaultValue, allowEmpty: false, keyReader, header, escHint);
    }

    /// <summary>Asks for a folder path (may be empty, meaning "project root").</summary>
    /// <param name="header"><c>null</c> renders the canonical banner, empty suppresses it, non-empty overrides it.</param>
    /// <exception cref="PromptCancelledException">Esc was pressed.</exception>
    /// <exception cref="OperationCanceledException">Ctrl+C was pressed.</exception>
    public static string AskFolder(
        IAnsiConsole console,
        string prompt,
        string? defaultValue = null,
        IKeyReader? keyReader = null,
        IReadOnlyList<string>? header = null,
        EscHint escHint = EscHint.Cancel)
    {
        ArgumentNullException.ThrowIfNull(console);

        return ReadText(console, prompt, defaultValue, allowEmpty: true, keyReader, header, escHint);
    }

    /// <summary>Yes/no confirmation with a default.</summary>
    /// <param name="header"><c>null</c> renders the canonical banner, empty suppresses it, non-empty overrides it.</param>
    /// <exception cref="PromptCancelledException">Esc was pressed.</exception>
    /// <exception cref="OperationCanceledException">Ctrl+C was pressed.</exception>
    public static bool Confirm(
        IAnsiConsole console,
        string prompt,
        bool defaultValue = true,
        IKeyReader? keyReader = null,
        IReadOnlyList<string>? header = null,
        EscHint escHint = EscHint.Cancel)
    {
        ArgumentNullException.ThrowIfNull(console);

        ScreenSession.EnsureOpen(console, keyReader is not null);

        using KeyScope scope = new(keyReader);
        IKeyReader reader = scope.Reader;

        string typed = string.Empty;
        bool? accepted = null;

        LiveDisplay live = console.Live(BuildConfirm(prompt, defaultValue, typed, header, escHint)).AutoClear(true);
        live.Start(ctx =>
        {
            ctx.UpdateTarget(BuildConfirm(prompt, defaultValue, typed, header, escHint));
            ctx.Refresh();

            while (accepted is null)
            {
                PickerKeyEvent evt = PickerKeyEvent.FromConsoleKeyInfo(reader.ReadKey());
                switch (evt.Action)
                {
                    case PickerAction.CancelProcess:
                        throw new OperationCanceledException();

                    case PickerAction.Cancel:
                        throw new PromptCancelledException();

                    case PickerAction.Backspace:
                        typed = string.Empty;
                        break;

                    case PickerAction.Insert:
                        char answer = char.ToLowerInvariant(evt.Character);
                        if (answer is 'y' or 'n')
                        {
                            typed = answer.ToString();
                        }

                        break;

                    case PickerAction.Select:
                        accepted = typed.Length == 0 ? defaultValue : typed == "y";
                        break;
                }

                if (accepted is null)
                {
                    ctx.UpdateTarget(BuildConfirm(prompt, defaultValue, typed, header, escHint));
                    ctx.Refresh();
                }
            }
        });

        return accepted ?? throw new InvalidOperationException("The confirmation ended without an answer.");
    }

    /// <summary>
    /// Shared line editor: printable characters (Unicode included), Backspace (drops the last
    /// text element), Enter (accept; empty takes <paramref name="defaultValue"/>, or the empty
    /// string when <paramref name="allowEmpty"/>, otherwise it is rejected silently and the
    /// prompt keeps editing), Esc (back/cancel) and Ctrl+C (process cancel).
    /// </summary>
    private static string ReadText(
        IAnsiConsole console,
        string prompt,
        string? defaultValue,
        bool allowEmpty,
        IKeyReader? keyReader,
        IReadOnlyList<string>? header,
        EscHint escHint)
    {
        ScreenSession.EnsureOpen(console, keyReader is not null);

        using KeyScope scope = new(keyReader);
        IKeyReader reader = scope.Reader;

        string value = string.Empty;
        string? accepted = null;

        LiveDisplay live = console.Live(BuildLine(prompt, defaultValue, value, header, escHint)).AutoClear(true);
        live.Start(ctx =>
        {
            ctx.UpdateTarget(BuildLine(prompt, defaultValue, value, header, escHint));
            ctx.Refresh();

            while (accepted is null)
            {
                PickerKeyEvent evt = PickerKeyEvent.FromConsoleKeyInfo(reader.ReadKey());
                switch (evt.Action)
                {
                    case PickerAction.CancelProcess:
                        throw new OperationCanceledException();

                    case PickerAction.Cancel:
                        throw new PromptCancelledException();

                    case PickerAction.Backspace:
                        value = DropLastTextElement(value);
                        break;

                    case PickerAction.Insert:
                        value += evt.Character;
                        break;

                    case PickerAction.Select:
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            accepted = value;
                        }
                        else if (defaultValue is not null)
                        {
                            accepted = defaultValue;
                        }
                        else if (allowEmpty)
                        {
                            accepted = string.Empty;
                        }
                        else
                        {
                            // A blank answer is rejected and the line restarts from scratch
                            // (AskName must never accept an empty name).
                            value = string.Empty;
                        }

                        break;
                }

                if (accepted is null)
                {
                    ctx.UpdateTarget(BuildLine(prompt, defaultValue, value, header, escHint));
                    ctx.Refresh();
                }
            }
        });

        return accepted ?? throw new InvalidOperationException("The prompt ended without an accepted value.");
    }

    /// <summary>Text prompt view: banner, input line (with cursor block) + contextual Esc hint.</summary>
    private static Rows BuildLine(string prompt, string? defaultValue, string value, IReadOnlyList<string>? header, EscHint escHint)
    {
        string head = ComposeHead(prompt, defaultDisplay: defaultValue, showChoices: false);
        return PromptRows($"{head}{Markup.Escape(value)}█", header, escHint);
    }

    /// <summary>Confirm view: banner + <c>Sure? [y/n] (y): y█</c> + contextual Esc hint.</summary>
    private static Rows BuildConfirm(string prompt, bool defaultValue, string typed, IReadOnlyList<string>? header, EscHint escHint)
    {
        string head = ComposeHead(prompt, defaultDisplay: defaultValue ? "y" : "n", showChoices: true);
        return PromptRows($"{head}{Markup.Escape(typed)}█", header, escHint);
    }

    private static Rows PromptRows(string inputLine, IReadOnlyList<string>? header, EscHint escHint)
    {
        List<IRenderable> elements = [];

        // Header: the canonical banner by default (override/suppress per call site).
        foreach (string line in header ?? AppHeader.Lines())
        {
            elements.Add(new Markup(line));
        }

        elements.Add(new Markup(inputLine));
        elements.Add(new Markup($"[{Theme.MutedMarkup}]{EscHintText.For(escHint)}[/]"));
        return new Rows(elements);
    }

    /// <summary>
    /// Prompt head in the Spectre layout: <c>Name: (App): </c> / <c>Sure? [y/n] (y): </c> —
    /// prompt text, optional details, then a colon when details exist or the text has none.
    /// </summary>
    private static string ComposeHead(string prompt, string? defaultDisplay, bool showChoices)
    {
        string raw = prompt.TrimEnd();
        string text = Markup.Escape(raw);
        bool hasDetails = false;

        if (showChoices)
        {
            text += $" [{Theme.MutedMarkup}][[y/n]][/]";
            hasDetails = true;
        }

        if (defaultDisplay is not null)
        {
            text += $" [{Theme.MutedMarkup}]({Markup.Escape(defaultDisplay)})[/]";
            hasDetails = true;
        }

        if (hasDetails || raw.Length == 0 || !":?.,;!".Contains(raw[^1]))
        {
            text += ":";
        }

        return text + " ";
    }

    /// <summary>
    /// Unicode-safe backspace: drops the last text element (grapheme), never half of an
    /// accented character, a surrogate pair or a combining sequence.
    /// </summary>
    internal static string DropLastTextElement(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        StringInfo info = new(value);
        int count = info.LengthInTextElements;
        return count <= 1 ? string.Empty : info.SubstringByTextElements(0, count - 1);
    }

    /// <summary>
    /// Key source of a prompt: the injected <see cref="IKeyReader"/> (tests), or the real
    /// console with Ctrl+C captured as a key — exactly like <see cref="FuzzyPicker{T}"/>.
    /// </summary>
    private sealed class KeyScope : IDisposable
    {
        private readonly bool _restore;

        public KeyScope(IKeyReader? keyReader)
        {
            Reader = keyReader ?? ConsoleKeyReader.Instance;
            _restore = keyReader is null;
            if (_restore)
            {
                PreviousTreatControlCAsInput = Console.TreatControlCAsInput;
                Console.TreatControlCAsInput = true;
            }
        }

        public IKeyReader Reader { get; }

        private bool PreviousTreatControlCAsInput { get; }

        public void Dispose()
        {
            if (_restore)
            {
                Console.TreatControlCAsInput = PreviousTreatControlCAsInput;
            }

            GC.SuppressFinalize(this);
        }
    }
}