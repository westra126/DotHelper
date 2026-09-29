using DotHelper.Ui;

using FluentAssertions;

using Spectre.Console.Testing;

namespace DotHelper.Tests.Unit;

/// <summary>
/// Custom prompts (user report 4): Esc cancels, Ctrl+C aborts, Backspace edits, defaults and
/// AllowEmpty keep their Spectre semantics. Keys are injected through <see cref="IKeyReader"/>
/// (no TTY) and the line repaints per keystroke via <c>Live</c>, so the snapshots assert the
/// visible fragments instead of a flat byte stream.
/// </summary>
public sealed class PromptsTests
{
    [Fact]
    public void AskName_prompts_and_returns_the_typed_value()
    {
        var console = new TestConsole();

        string value = Prompts.AskName(
            console, "Name:", keyReader: Keys('C', 'u', 's', 't', 'o', 'm', 'e', 'r', 'I', 'd', ConsoleKey.Enter));

        value.Should().Be("CustomerId");
        console.Output.Should().Contain("Name:");
        console.Output.Should().Contain("CustomerId", "the typed value is echoed");
    }

    [Fact]
    public void AskName_uses_the_default_value_on_empty_input()
    {
        var console = new TestConsole();

        string value = Prompts.AskName(
            console, "Name:", "App", Keys(ConsoleKey.Enter));

        value.Should().Be("App");
        console.Output.Should().Contain("(App)", "the default is visible in the prompt");
    }

    [Fact]
    public void AskName_reprompts_until_a_value_is_given()
    {
        var console = new TestConsole();

        string value = Prompts.AskName(
            console, "Name:", keyReader: Keys(ConsoleKey.Enter, 'L', 'i', 'b', ConsoleKey.Enter));

        value.Should().Be("Lib", "the empty answer is rejected and the prompt asks again");
    }

    [Fact]
    public void AskName_rejects_whitespace_only_answers()
    {
        // Preserved semantics: the old TextPrompt validator refused blank names.
        var console = new TestConsole();

        string value = Prompts.AskName(
            console, "Name:", keyReader: Keys(' ', ConsoleKey.Enter, 'L', 'i', 'b', ConsoleKey.Enter));

        value.Should().Be("Lib");
    }

    [Fact]
    public void AskName_shows_the_esc_hint()
    {
        var console = new TestConsole();

        Action act = () => Prompts.AskName(console, "Name:", keyReader: Keys(ConsoleKey.Escape));

        act.Should().Throw<PromptCancelledException>();
        console.Output.Should().Contain("Esc cancel", "the default hint says what Esc does");
    }

    [Fact]
    public void The_canonical_banner_renders_in_prompts_by_default()
    {
        // Universal-header report: Name/Folder/Confirm dialogs show the banner with no call-site
        // wiring (AppHeader is the single source).
        var console = new TestConsole();

        string value = Prompts.AskName(
            console, "Name:", keyReader: Keys('A', ConsoleKey.Enter));

        value.Should().Be("A");
        console.Output.Should().Contain("DotHelper", "the banner is rendered by default");
        console.Output.Should().Contain("asistente para .NET");
        console.Output.IndexOf("DotHelper", StringComparison.Ordinal)
            .Should().BeLessThan(console.Output.IndexOf("Name:", StringComparison.Ordinal), "the banner is above the input line");
    }

    [Fact]
    public void The_banner_renders_in_confirm_prompts_too()
    {
        var console = new TestConsole();

        bool answer = Prompts.Confirm(console, "Sure?", defaultValue: true, keyReader: Keys(ConsoleKey.Enter));

        answer.Should().BeTrue();
        console.Output.Should().Contain("asistente para .NET", "confirmation dialogs show the banner as well");
    }

    [Fact]
    public void The_prompt_header_can_be_overridden_or_suppressed()
    {
        var overriden = new TestConsole();
        var suppressed = new TestConsole();

        Prompts.AskName(
            overriden, "Name:", keyReader: Keys('A', ConsoleKey.Enter), header: ["[grey]custom header[/]"]);
        Prompts.AskFolder(
            suppressed, "Folder:", keyReader: Keys(ConsoleKey.Enter), header: []);

        overriden.Output.Should().Contain("custom header");
        overriden.Output.Should().NotContain("asistente para .NET", "an override replaces the banner");
        suppressed.Output.Should().NotContain("asistente para .NET", "an empty header suppresses the banner");
        suppressed.Output.Should().Contain("Folder:");
    }

    [Theory]
    [InlineData(EscHint.Cancel, "Esc cancel")]
    [InlineData(EscHint.Back, "Esc back")]
    [InlineData(EscHint.Exit, "Esc exit")]
    public void The_esc_hint_of_a_prompt_is_contextual(EscHint hint, string expected)
    {
        var console = new TestConsole();

        Action act = () => Prompts.AskName(
            console, "Name:", keyReader: Keys(ConsoleKey.Escape), escHint: hint);

        act.Should().Throw<PromptCancelledException>();
        console.Output.Should().Contain(expected);
    }

    [Fact]
    public void AskFolder_allows_an_empty_answer()
    {
        // "Enter = project root" semantics (PLAN.md §5.3): empty is a valid answer.
        var console = new TestConsole();

        string value = Prompts.AskFolder(
            console, "Folder (Enter = project root):", keyReader: Keys(ConsoleKey.Enter));

        value.Should().BeEmpty();
    }

    [Fact]
    public void AskFolder_returns_the_typed_folder()
    {
        var console = new TestConsole();

        string value = Prompts.AskFolder(
            console, "Folder:", keyReader: Keys('D', 'o', 'm', 'a', 'i', 'n', '/', 'C', 'u', 's', 't', 'o', 'm', 'e', 'r', 's', ConsoleKey.Enter));

        value.Should().Be("Domain/Customers");
    }

    [Fact]
    public void Backspace_edits_the_buffer_before_accepting()
    {
        var console = new TestConsole();

        string value = Prompts.AskName(
            console, "Name:", keyReader: Keys('A', 'c', ConsoleKey.Backspace, 'l', 'p', 'h', 'a', ConsoleKey.Enter));

        value.Should().Be("Alpha");
    }

    [Fact]
    public void Accented_and_unicode_characters_survive_the_echo()
    {
        var console = new TestConsole();

        string value = Prompts.AskName(
            console, "Name:", keyReader: Keys('C', 'a', 'f', 'é', 'ñ', ConsoleKey.Enter));

        value.Should().Be("Caféñ");
    }

    [Fact]
    public void Esc_cancels_the_prompt_with_a_clean_exception()
    {
        var console = new TestConsole();

        Action act = () => Prompts.AskName(
            console, "Name:", keyReader: Keys('B', 'l', 'a', 'h', ConsoleKey.Escape));

        act.Should().Throw<PromptCancelledException>("Esc must cancel text dialogs (user report 4)");
    }

    [Fact]
    public void Esc_cancels_folder_and_confirm_prompts_too()
    {
        var console = new TestConsole();

        Action folder = () => Prompts.AskFolder(console, "Folder:", keyReader: Keys(ConsoleKey.Escape));
        Action confirm = () => Prompts.Confirm(console, "Sure?", defaultValue: true, keyReader: Keys(ConsoleKey.Escape));

        folder.Should().Throw<PromptCancelledException>();
        confirm.Should().Throw<PromptCancelledException>();
    }

    [Fact]
    public void Ctrl_c_surfaces_as_operation_canceled()
    {
        var console = new TestConsole();

        Action act = () => Prompts.AskName(
            console, "Name:", keyReader: Keys(new ConsoleKeyInfo('\u0003', ConsoleKey.C, false, false, true)));

        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void Confirm_accepts_yes()
    {
        var console = new TestConsole();

        bool answer = Prompts.Confirm(
            console, "Sure?", defaultValue: true, keyReader: Keys('y', ConsoleKey.Enter));

        answer.Should().BeTrue();
        console.Output.Should().Contain("[y/n]", "the choices are visible like the old ConfirmationPrompt");
    }

    [Fact]
    public void Confirm_accepts_no()
    {
        var console = new TestConsole();

        bool answer = Prompts.Confirm(
            console, "Sure?", defaultValue: true, keyReader: Keys('n', ConsoleKey.Enter));

        answer.Should().BeFalse();
    }

    [Fact]
    public void Confirm_uses_the_default_on_empty_input()
    {
        var console = new TestConsole();

        bool answer = Prompts.Confirm(
            console, "Sure?", defaultValue: true, keyReader: Keys(ConsoleKey.Enter));

        answer.Should().BeTrue();
    }

    [Fact]
    public void Confirm_defaults_to_no_when_configured()
    {
        bool answer = Prompts.Confirm(
            new TestConsole(), "Sure?", defaultValue: false, keyReader: Keys(ConsoleKey.Enter));

        answer.Should().BeFalse();
    }

    [Fact]
    public void DropLastTextElement_is_unicode_safe()
    {
        Prompts.DropLastTextElement("Café").Should().Be("Caf");
        Prompts.DropLastTextElement("Añ").Should().Be("A");
        Prompts.DropLastTextElement("A").Should().BeEmpty();
        Prompts.DropLastTextElement(string.Empty).Should().BeEmpty();
    }

    private static IKeyReader Keys(params object[] keys) => ScriptedKeyReader.From(keys);
}