using DotHelper.Ui;

using FluentAssertions;

namespace DotHelper.Tests.Unit;

/// <summary>
/// <see cref="FlowNavigator"/> (back-navigation report): the pure step machine behind every
/// interactive flow — only user-visible steps (real pickers/prompts) are rewind targets,
/// auto-resolved steps are transparent, and Esc at the first visible step exits the flow.
/// </summary>
public sealed class FlowNavigatorTests
{
    [Fact]
    public void The_first_visible_step_asks_with_the_first_step_hint()
    {
        FlowNavigator nav = new(EscHint.Back);

        EscHint hint = nav.Ask();

        hint.Should().Be(EscHint.Back, "the first interactive step of a wizard flow goes back to its menu");
    }

    [Fact]
    public void A_direct_command_first_step_asks_with_the_cancel_hint()
    {
        FlowNavigator nav = new(); // default: EscHint.Cancel

        nav.Ask().Should().Be(EscHint.Cancel);
    }

    [Fact]
    public void Esc_at_the_first_visible_step_exits_the_flow()
    {
        FlowNavigator nav = new();
        nav.Ask();

        bool rewound = nav.TryRewind();

        rewound.Should().BeFalse("there is no previous step to go back to");
    }

    [Fact]
    public void Esc_at_a_later_step_rewinds_to_the_previous_visible_one()
    {
        FlowNavigator nav = new();
        nav.Ask(); // step 0
        nav.Next();
        EscHint hint = nav.Ask(); // step 1

        hint.Should().Be(EscHint.Back, "intermediate steps say 'Esc back'");
        nav.TryRewind().Should().BeTrue();
        nav.Step.Should().Be(0, "the flow re-runs the previous visible step");
    }

    [Fact]
    public void Auto_resolved_steps_are_transparent_for_rewinding()
    {
        // Step 0 auto (no ask), step 1 visible, step 2 auto (no ask), step 3 visible.
        FlowNavigator nav = new();
        nav.Next(); // auto step 0

        nav.Ask(); // step 1 asks
        nav.Next();
        nav.Next(); // auto step 2

        nav.Ask(); // step 3 asks
        nav.TryRewind().Should().BeTrue();
        nav.Step.Should().Be(1, "the auto step 2 is not a rewind target");
        nav.TryRewind().Should().BeFalse("step 1 is the first visible step");
    }

    [Fact]
    public void A_rewound_first_step_keeps_its_first_step_hint()
    {
        FlowNavigator nav = new();
        nav.Ask(); // step 0
        nav.Next();
        nav.Ask(); // step 1
        nav.TryRewind();

        // Re-ask of step 0 after the rewind: still the first visible step → same semantics.
        nav.Ask().Should().Be(EscHint.Cancel);
        nav.TryRewind().Should().BeFalse();
    }

    [Fact]
    public void Rewinding_twice_walks_back_through_every_visible_step()
    {
        FlowNavigator nav = new();
        nav.Ask(); // step 0
        nav.Next();
        nav.Ask(); // step 1
        nav.Next();
        nav.Ask(); // step 2

        nav.TryRewind().Should().BeTrue();
        nav.Step.Should().Be(1);
        nav.TryRewind().Should().BeTrue();
        nav.Step.Should().Be(0);
        nav.TryRewind().Should().BeFalse();
    }

    [Fact]
    public void Replaying_forward_reaches_the_same_steps_again()
    {
        // After a rewind the flow replays the steps: answers were kept by the flow itself and
        // are offered as defaults; the machine simply accepts Next() again.
        FlowNavigator nav = new();
        nav.Ask();
        nav.Next();
        nav.Ask();
        nav.TryRewind();

        nav.Next(); // step 0 answered again
        nav.Ask().Should().Be(EscHint.Back, "step 1 is again an intermediate step");
    }
}