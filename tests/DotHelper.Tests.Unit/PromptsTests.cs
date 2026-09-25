using DotHelper.Ui;

using FluentAssertions;

using Spectre.Console.Testing;

namespace DotHelper.Tests.Unit;

/// <summary>
/// Prompt snapshots with <see cref="TestConsole"/> (PLAN.md §7 Fase 5). Prompt lines are flat
/// writes (no <c>Live</c>), so byte-exact snapshots are stable here; keys come from
/// <c>TestConsole.Input</c> (Spectre's own <c>PushTextWithEnter</c>).
/// </summary>
public sealed class PromptsTests
{
    [Fact]
    public void AskName_prompts_and_returns_the_typed_value()
    {
        var console = new TestConsole();
        console.Input.PushTextWithEnter("CustomerId");

        string value = Prompts.AskName(console, "Name:");

        value.Should().Be("CustomerId");
        Snapshot(console).Should().Be("Name: CustomerId\n");
    }

    [Fact]
    public void AskName_uses_the_default_value_on_empty_input()
    {
        var console = new TestConsole();
        console.Input.PushTextWithEnter("");

        string value = Prompts.AskName(console, "Name:", "App");

        value.Should().Be("App");
        Snapshot(console).Should().Be("Name: (App): App\n");
    }

    [Fact]
    public void AskName_reprompts_until_a_value_is_given()
    {
        var console = new TestConsole();
        console.Input.PushKey(ConsoleKey.Enter); // rejected: empty
        console.Input.PushTextWithEnter("Lib");

        string value = Prompts.AskName(console, "Name:");

        value.Should().Be("Lib", "the empty answer is rejected and the prompt asks again");
        // The failed attempt is re-rendered on the same line, so the snapshot holds the final state.
        Snapshot(console).Should().Be("Name: Lib\n");
    }

    [Fact]
    public void AskFolder_allows_an_empty_answer()
    {
        // "Enter = project root" semantics (PLAN.md §5.3): empty is a valid answer.
        var console = new TestConsole();
        console.Input.PushKey(ConsoleKey.Enter);

        string value = Prompts.AskFolder(console, "Folder (Enter = project root):");

        value.Should().BeEmpty();
        Snapshot(console).Should().Be("Folder (Enter = project root): \n");
    }

    [Fact]
    public void AskFolder_returns_the_typed_folder()
    {
        var console = new TestConsole();
        console.Input.PushTextWithEnter("Domain/Customers");

        string value = Prompts.AskFolder(console, "Folder:");

        value.Should().Be("Domain/Customers");
        Snapshot(console).Should().Be("Folder: Domain/Customers\n");
    }

    [Fact]
    public void Confirm_accepts_yes()
    {
        var console = new TestConsole();
        console.Input.PushTextWithEnter("y");

        bool answer = Prompts.Confirm(console, "Sure?", defaultValue: true);

        answer.Should().BeTrue();
        Snapshot(console).Should().Be("Sure? [y/n] (y): y\n");
    }

    [Fact]
    public void Confirm_accepts_no()
    {
        var console = new TestConsole();
        console.Input.PushTextWithEnter("n");

        bool answer = Prompts.Confirm(console, "Sure?", defaultValue: true);

        answer.Should().BeFalse();
        Snapshot(console).Should().Be("Sure? [y/n] (y): n\n");
    }

    [Fact]
    public void Confirm_uses_the_default_on_empty_input()
    {
        var console = new TestConsole();
        console.Input.PushKey(ConsoleKey.Enter);

        bool answer = Prompts.Confirm(console, "Sure?", defaultValue: true);

        answer.Should().BeTrue();
        Snapshot(console).Should().Be("Sure? [y/n] (y): y\n");
    }

    private static string Snapshot(TestConsole console) =>
        console.Output.Replace("\r\n", "\n");
}