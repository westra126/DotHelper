using DotHelper.Cli;
using DotHelper.Ui;

using FluentAssertions;

using Spectre.Console;
using Spectre.Console.Testing;

namespace DotHelper.Tests.Unit;

/// <summary>
/// Root wizard tests (PLAN.md §7 Fase 5: "snapshot de prompts con TestConsole de Spectre").
/// The <see cref="FuzzyPicker{T}"/> is driven through an injected <see cref="IKeyReader"/>, so no
/// TTY is required. Rendering strategy: the picker goes through <c>Spectre.Console.Live</c>, which
/// on <see cref="TestConsole"/> accumulates one text block per frame (verified empirically), so
/// picker output is asserted with contains-checks; only flat zones (the header) get a byte-exact
/// snapshot.
/// </summary>
public sealed class RootWizardTests
{
    [Fact]
    public void Header_snapshot_is_stable()
    {
        var console = new TestConsole();
        console.Width(120); // no wrapping: the snapshot must be byte-exact

        RootWizard.RenderHeader(console, "0.0.0-test");

        console.Output.Replace("\r\n", "\n").Should().Be(
            "DotHelper 0.0.0-test — asistente para .NET\n" +
            "↑/↓ mover · Enter seleccionar · Esc cancelar · Tab detalle · escribir para filtrar\n");
    }

    [Fact]
    public async Task Root_menu_renders_the_six_actions_and_key_hints()
    {
        Harness h = NewHarness(ScriptedKeyReader.From(ConsoleKey.Escape));

        int exit = await h.RunAsync();

        exit.Should().Be(0, "Esc cancels cleanly");
        foreach (string title in SixActions)
        {
            h.Console.Output.Should().Contain(title);
        }

        h.Console.Output.Should().Contain("Enter select", "the picker prints the key hints");
        h.Console.Output.Should().Contain("Cancelled.");
    }

    [Fact]
    public void Root_menu_lists_exactly_the_six_plan_actions()
    {
        WizardMenu.Root.Select(static i => i.Title).Should().Equal(SixActions);
    }

    [Fact]
    public async Task Typing_cl_then_enter_selects_nueva_clase_o_item()
    {
        Harness h = NewHarness(ScriptedKeyReader.From('c', 'l', ConsoleKey.Enter));

        int exit = await h.RunAsync();

        exit.Should().Be(42, "the wizard returns the exit code of the executed flow");
        h.Executed.Should().ContainSingle();
        h.Executed[0].Id.Should().Be("new.item");
        h.Executed[0].Title.Should().Be("Nueva clase o item");
    }

    [Fact]
    public async Task Query_preselects_the_action_in_the_picker()
    {
        // `dh --query "cl"` seeds the filter; Enter alone selects the top-ranked action.
        Harness h = NewHarness(ScriptedKeyReader.From(ConsoleKey.Enter));

        int exit = await h.RunAsync("cl");

        exit.Should().Be(42);
        h.Executed.Should().ContainSingle();
        h.Executed[0].Id.Should().Be("new.item");
    }

    [Theory]
    [InlineData("nuget", "nuget.search", "search", "add", "remove", "list")]
    [InlineData("ref", "references.add", "add-ref", "remove-ref")]
    [InlineData("list", "list.templates", "templates", "projects", "solutions")]
    public async Task Sub_menus_render_their_options_and_dispatch_the_leaf(
        string filter,
        string expectedLeafId,
        params string[] expectedOptions)
    {
        List<object> keys = [];
        foreach (char c in filter)
        {
            keys.Add(c);
        }

        keys.Add(ConsoleKey.Enter);
        keys.Add(ConsoleKey.Enter);

        Harness h = NewHarness(ScriptedKeyReader.From([.. keys]));

        int exit = await h.RunAsync();

        exit.Should().Be(42);
        h.Executed.Should().ContainSingle();
        h.Executed[0].Id.Should().Be(expectedLeafId);
        foreach (string option in expectedOptions)
        {
            h.Console.Output.Should().Contain(option, $"the sub-menu lists '{option}'");
        }
    }

    [Fact]
    public async Task Esc_on_a_sub_menu_cancels_without_executing()
    {
        Harness h = NewHarness(ScriptedKeyReader.From(
            'n', 'u', 'g', 'e', 't', ConsoleKey.Enter, ConsoleKey.Escape));

        int exit = await h.RunAsync();

        exit.Should().Be(0);
        h.Executed.Should().BeEmpty();
        h.Console.Output.Should().Contain("Cancelled.");
    }

    [Fact]
    public async Task Ctrl_c_surfaces_as_operation_canceled()
    {
        Harness h = NewHarness(
            ScriptedKeyReader.From(new ConsoleKeyInfo('\u0003', ConsoleKey.C, false, false, true)));

        Func<Task> act = async () => await h.RunAsync();

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Without_tty_the_help_fallback_runs_instead_of_reading_keys()
    {
        var console = new TestConsole();
        bool helpShown = false;
        var wizard = new RootWizard(
            console,
            keyReader: null,
            executor: (_, _) => throw new InvalidOperationException("must not execute actions"),
            showHelp: () =>
            {
                helpShown = true;
                console.WriteLine("HELP FALLBACK");
                return 77;
            },
            inputRedirected: true,
            version: "0.0.0-test");

        int exit = await wizard.RunAsync(query: null, TestContext.Current.CancellationToken);

        exit.Should().Be(77);
        helpShown.Should().BeTrue("no TTY must fall back to the help screen");
        console.Output.Should().Contain("HELP FALLBACK");
        console.Output.Should().NotContain("Nueva solución", "the menu must not be rendered without a TTY");
    }

    [Fact]
    public async Task Without_tty_and_a_query_the_help_fallback_still_runs()
    {
        var console = new TestConsole();
        var wizard = new RootWizard(
            console,
            keyReader: null,
            executor: (_, _) => throw new InvalidOperationException("must not execute actions"),
            showHelp: () => 0,
            inputRedirected: true,
            version: "0.0.0-test");

        int exit = await wizard.RunAsync("cl", TestContext.Current.CancellationToken);

        exit.Should().Be(0);
    }

    [Fact]
    public async Task An_injected_key_reader_bypasses_the_tty_guard()
    {
        // Tests run with redirected stdin; the injected reader must still drive the menu.
        Harness h = NewHarness(ScriptedKeyReader.From(ConsoleKey.Escape), inputRedirected: true);

        int exit = await h.RunAsync();

        exit.Should().Be(0);
        h.HelpShown.Should().BeFalse();
        h.Console.Output.Should().Contain("Nueva solución");
    }

    [Fact]
    public async Task Dispatcher_rejects_unknown_action_ids()
    {
        var unknown = new WizardItem
        {
            Id = "nope",
            Title = "Nope",
            Description = "x",
            Command = "dh nope",
            Keywords = [],
        };

        Func<Task<int>> act = () => WizardDispatcher.RunAsync(unknown, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void Menu_fields_prefer_keywords_over_title()
    {
        WizardItem item = WizardMenu.Root.Single(static i => i.Id == "new.item");

        IReadOnlyList<WeightedField> fields = WizardMenu.Fields(item);

        fields[0].Weight.Should().Be(WeightedField.ShortNameWeight);
        fields[^2].Text.Should().Be(item.Title);
        fields[^2].Weight.Should().Be(WeightedField.NameWeight);
        fields[^1].Weight.Should().Be(WeightedField.TagWeight);
        WizardMenu.Detail(item).Should().Contain(d => d.Contains("dh item", StringComparison.Ordinal));
    }

    private static readonly string[] SixActions =
    [
        "Nueva solución", "Nuevo proyecto", "Nueva clase o item", "NuGet", "Referencias", "Listar",
    ];

    private static Harness NewHarness(IKeyReader keys, bool? inputRedirected = false) => new(keys, inputRedirected);

    /// <summary>Wizard wired with a fake executor that records the dispatched actions.</summary>
    private sealed class Harness
    {
        public Harness(IKeyReader keys, bool? inputRedirected)
        {
            Console = new TestConsole();
            Wizard = new RootWizard(
                Console,
                keyReader: keys,
                executor: (item, _) =>
                {
                    Executed.Add(item);
                    return Task.FromResult(42);
                },
                showHelp: () =>
                {
                    HelpShown = true;
                    return 0;
                },
                inputRedirected: inputRedirected,
                version: "0.0.0-test");
        }

        public TestConsole Console { get; }

        public List<WizardItem> Executed { get; } = [];

        public bool HelpShown { get; private set; }

        public RootWizard Wizard { get; }

        public Task<int> RunAsync(string? query = null) =>
            Wizard.RunAsync(query, TestContext.Current.CancellationToken);
    }
}