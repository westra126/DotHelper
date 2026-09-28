using DotHelper.Ui;

using FluentAssertions;

using Spectre.Console.Testing;

namespace DotHelper.Tests.Unit;

/// <summary>
/// <see cref="FuzzyPicker{T}"/> driven by an injected <see cref="IKeyReader"/> (Fase 5
/// testability refactor). Verifies that the interactive loop behaves exactly as with the real
/// console: typing filters, Enter selects, Esc cancels, Tab shows detail.
/// </summary>
public sealed class FuzzyPickerTests
{
    [Fact]
    public void Enter_selects_the_highlighted_item()
    {
        var console = new TestConsole();
        var picker = NewPicker(console, ScriptedKeyReader.From(ConsoleKey.DownArrow, ConsoleKey.Enter));

        string? picked = picker.Pick(TestContext.Current.CancellationToken);

        picked.Should().Be("Bravo", "DownArrow moves the selection to the second row");
    }

    [Fact]
    public void Typing_filters_the_list_and_enter_selects_the_match()
    {
        var console = new TestConsole();
        var picker = NewPicker(console, ScriptedKeyReader.From('c', 'h', ConsoleKey.Enter));

        string? picked = picker.Pick(TestContext.Current.CancellationToken);

        picked.Should().Be("Charlie");
    }

    [Fact]
    public void Esc_cancels_and_returns_default()
    {
        var console = new TestConsole();
        var picker = NewPicker(console, ScriptedKeyReader.From(ConsoleKey.Escape));

        string? picked = picker.Pick(TestContext.Current.CancellationToken);

        picked.Should().BeNull();
    }

    [Fact]
    public void Tab_shows_the_detail_lines()
    {
        var console = new TestConsole();
        var picker = NewPicker(console, ScriptedKeyReader.From(ConsoleKey.Tab, ConsoleKey.Escape));

        picker.Pick(TestContext.Current.CancellationToken);

        console.Output.Should().Contain("detail");
        console.Output.Should().Contain("item: Alpha");
    }

    [Fact]
    public void An_exhausted_key_sequence_fails_loudly()
    {
        var console = new TestConsole();
        var picker = NewPicker(console, new ScriptedKeyReader([]));

        Action act = () => picker.Pick(TestContext.Current.CancellationToken);

        act.Should().Throw<InvalidOperationException>().WithMessage("*no more keys*");
    }

    [Fact]
    public void A_redirected_console_without_keys_still_requires_an_initial_query()
    {
        // Unchanged Fase 2 contract: with stdin redirected and no key reader, no ReadKey happens.
        // (Guarded: when the test host does provide a TTY the picker would block on the real
        // console, so only the redirected case is asserted.)
        if (!Console.IsInputRedirected)
        {
            Assert.Skip("The test host has a real TTY; the redirected-stdin contract needs redirected stdin.");
        }

        var console = new TestConsole();
        var picker = new FuzzyPicker<string>(
            console,
            ["Alpha"],
            new FuzzyPickerOptions<string>
            {
                PrimaryText = static s => s,
                Fields = static s => [new WeightedField(s, WeightedField.NameWeight)],
                Title = "demo",
            });

        Action act = () => picker.Pick(TestContext.Current.CancellationToken);

        act.Should().Throw<InvalidOperationException>().WithMessage("*interactive TTY*");
    }

    [Fact]
    public void First_frame_is_painted_before_any_key_is_read()
    {
        // Fix (user report): the picker must render immediately, not wait for a keystroke.
        var console = new TestConsole();
        string outputAtFirstKey = string.Empty;
        var keys = new ProbingKeyReader(() =>
        {
            outputAtFirstKey = console.Output;
            return new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false);
        });

        NewPicker(console, keys).Pick(TestContext.Current.CancellationToken);

        outputAtFirstKey.Should().Contain("Alpha", "the list must already be on screen");
        outputAtFirstKey.Should().Contain("Enter select", "the hints must already be on screen");
    }

    [Fact]
    public void Injected_key_reader_keeps_the_configured_page_size()
    {
        // Deterministic tests: a tiny console must NOT shrink the page when keys are injected.
        var console = new TestConsole();
        console.Height(8);
        List<string> items = Enumerable.Range(1, 20).Select(static i => $"Item{i:00}").ToList();
        var picker = new FuzzyPicker<string>(
            console,
            items,
            new FuzzyPickerOptions<string>
            {
                PrimaryText = static s => s,
                Fields = static s => [new WeightedField(s, WeightedField.NameWeight)],
                Title = "demo",
                PageSize = 12,
                KeyReader = ScriptedKeyReader.From(ConsoleKey.Escape),
            });

        picker.Pick(TestContext.Current.CancellationToken);

        console.Output.Should().Contain("Item12", "the configured page size (12) must be honoured");
    }

    [Fact]
    public void Injected_key_reader_never_enters_the_alternate_screen()
    {
        var console = new TestConsole();
        console.EmitAnsiSequences();
        var picker = NewPicker(console, ScriptedKeyReader.From(ConsoleKey.Escape));

        picker.Pick(TestContext.Current.CancellationToken);

        console.Output.Should().NotContain("\u001b[?1049h", "tests must not touch the terminal buffer");
        console.Output.Should().NotContain("\u001b[?1049l");
    }

    [Fact]
    public void Header_lines_render_inside_the_picker_frame()
    {
        // User report 1: the wizard banner must live inside the picker view (visible while the
        // alternate screen is open), not on the primary screen the session hides.
        var console = new TestConsole();
        var picker = new FuzzyPicker<string>(
            console,
            ["Alpha"],
            new FuzzyPickerOptions<string>
            {
                PrimaryText = static s => s,
                Fields = static s => [new WeightedField(s, WeightedField.NameWeight)],
                Title = "demo",
                Header = ["[bold cyan]DotHelper[/] [grey]1.2.3[/]", "[grey]hint line[/]"],
                KeyReader = ScriptedKeyReader.From(ConsoleKey.Escape),
            });

        picker.Pick(TestContext.Current.CancellationToken);

        console.Output.Should().Contain("DotHelper");
        console.Output.Should().Contain("1.2.3");
        console.Output.Should().Contain("hint line", "the header renders as the first lines of the frame");
    }

    [Fact]
    public void Detail_keeps_the_configured_page_size_with_an_injected_reader()
    {
        // Determinism contract: with an injected key reader the configured PageSize wins over
        // the console height, so the detail block must NOT shrink the list here. The
        // window-fitting case is covered by PickerLayoutTests (pure) and by the PTY evidence.
        var console = new TestConsole();
        console.Height(10);
        List<string> items = Enumerable.Range(1, 20).Select(static i => $"Item{i:00}").ToList();
        var picker = new FuzzyPicker<string>(
            console,
            items,
            new FuzzyPickerOptions<string>
            {
                PrimaryText = static s => s,
                Fields = static s => [new WeightedField(s, WeightedField.NameWeight)],
                DetailLines = static _ => new[] { "d1", "d2", "d3", "d4", "d5" },
                Title = "demo",
                PageSize = 12,
                KeyReader = ScriptedKeyReader.From(ConsoleKey.Tab, ConsoleKey.Escape),
            });

        picker.Pick(TestContext.Current.CancellationToken);

        console.Output.Should().Contain("detail");
        console.Output.Should().Contain("d5", "the whole detail block is shown");
        console.Output.Should().Contain("Item12", "the configured page size is kept for tests");
    }

    /// <summary>Key reader that probes the console output at the first read.</summary>
    private sealed class ProbingKeyReader : IKeyReader
    {
        private readonly Func<ConsoleKeyInfo> _onFirst;

        public ProbingKeyReader(Func<ConsoleKeyInfo> onFirst) => _onFirst = onFirst;

        public ConsoleKeyInfo ReadKey() => _onFirst();
    }

    private static FuzzyPicker<string> NewPicker(TestConsole console, IKeyReader keys) =>
        new(
            console,
            ["Alpha", "Bravo", "Charlie"],
            new FuzzyPickerOptions<string>
            {
                PrimaryText = static s => s,
                Fields = static s => [new WeightedField(s, WeightedField.NameWeight)],
                DetailLines = static s => [$"item: {s}"],
                Title = "demo",
                KeyReader = keys,
            });
}