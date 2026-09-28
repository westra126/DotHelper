using DotHelper.Ui;

using FluentAssertions;

namespace DotHelper.Tests.Unit;

/// <summary>
/// Pure layout math of the full-height picker (Fase: alternate screen + window-filling list).
/// </summary>
public sealed class PickerLayoutTests
{
    [Fact]
    public void Unknown_height_keeps_the_configured_page_size()
    {
        FuzzyPicker<string>.ComputeLayout(consoleHeight: 0, 12, showDetail: false, 0, itemCount: 50)
            .Should().Be(new PickerLayout(12, 0));
        FuzzyPicker<string>.ComputeLayout(consoleHeight: -1, 12, showDetail: false, 0, itemCount: 50)
            .Should().Be(new PickerLayout(12, 0));
    }

    [Fact]
    public void List_fills_the_window_with_room_for_title_hints_counter_and_spare_line()
    {
        // height 24: 3 fixed lines + 1 counter line (50 items do not fit) => 20 rows.
        PickerLayout layout = FuzzyPicker<string>.ComputeLayout(24, 12, showDetail: false, 0, itemCount: 50);

        layout.PageSize.Should().Be(20);
        layout.DetailLineLimit.Should().Be(0);
    }

    [Fact]
    public void No_counter_line_when_everything_fits()
    {
        // height 24, 10 items: no counter → page may be 21 but the list only has 10 rows.
        PickerLayout layout = FuzzyPicker<string>.ComputeLayout(24, 12, showDetail: false, 0, itemCount: 10);

        layout.PageSize.Should().Be(21);
    }

    [Fact]
    public void Detail_block_shrinks_the_list()
    {
        // height 24, detail rule + 4 lines + counter => 24-3-1-4-1 = 15 rows.
        PickerLayout layout = FuzzyPicker<string>.ComputeLayout(24, 12, showDetail: true, 4, itemCount: 50);

        layout.PageSize.Should().Be(15);
        layout.DetailLineLimit.Should().Be(4);
    }

    [Fact]
    public void Detail_lines_are_dropped_before_the_list_disappears()
    {
        // Tiny window: the list keeps at least one row, detail shrinks, hints survive.
        PickerLayout layout = FuzzyPicker<string>.ComputeLayout(8, 12, showDetail: true, 6, itemCount: 50);

        layout.PageSize.Should().BeGreaterThanOrEqualTo(1);
        layout.DetailLineLimit.Should().BeLessThan(6, "detail yields space to the list");
        (layout.PageSize + layout.DetailLineLimit + 3 + 1 + 1).Should()
            .BeLessThanOrEqualTo(8 + 2, "title/hints/counter/detail must fit the window");
    }

    [Fact]
    public void Absurdly_small_window_falls_back_to_one_row()
    {
        PickerLayout layout = FuzzyPicker<string>.ComputeLayout(3, 12, showDetail: true, 6, itemCount: 50);

        layout.PageSize.Should().Be(1);
        layout.DetailLineLimit.Should().Be(0);
    }
}