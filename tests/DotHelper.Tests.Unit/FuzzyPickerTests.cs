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
            return;
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