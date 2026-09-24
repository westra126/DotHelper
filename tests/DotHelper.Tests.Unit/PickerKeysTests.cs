using DotHelper.Ui;

using FluentAssertions;

namespace DotHelper.Tests.Unit;

public sealed class PickerKeysTests
{
    [Theory]
    [InlineData(ConsoleKey.UpArrow, PickerAction.MoveUp)]
    [InlineData(ConsoleKey.DownArrow, PickerAction.MoveDown)]
    [InlineData(ConsoleKey.Enter, PickerAction.Select)]
    [InlineData(ConsoleKey.Escape, PickerAction.Cancel)]
    [InlineData(ConsoleKey.Tab, PickerAction.ToggleDetail)]
    [InlineData(ConsoleKey.Backspace, PickerAction.Backspace)]
    public void Maps_navigation_keys(ConsoleKey key, PickerAction expected)
    {
        var keyInfo = new ConsoleKeyInfo('\0', key, false, false, false);

        PickerKeyEvent evt = PickerKeyEvent.FromConsoleKeyInfo(keyInfo);

        evt.Action.Should().Be(expected);
        evt.Character.Should().Be('\0');
    }

    [Fact]
    public void Maps_plain_characters_to_insert()
    {
        var keyInfo = new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false);

        PickerKeyEvent evt = PickerKeyEvent.FromConsoleKeyInfo(keyInfo);

        evt.Action.Should().Be(PickerAction.Insert);
        evt.Character.Should().Be('a');
    }

    [Fact]
    public void Maps_ctrl_c_to_process_cancellation()
    {
        var keyInfo = new ConsoleKeyInfo('\u0003', ConsoleKey.C, false, false, true);

        PickerKeyEvent evt = PickerKeyEvent.FromConsoleKeyInfo(keyInfo);

        evt.Action.Should().Be(PickerAction.CancelProcess);
    }

    [Fact]
    public void Maps_other_control_keys_to_none()
    {
        var keyInfo = new ConsoleKeyInfo('\0', ConsoleKey.Home, false, false, false);

        PickerKeyEvent evt = PickerKeyEvent.FromConsoleKeyInfo(keyInfo);

        evt.Action.Should().Be(PickerAction.None);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0, 12)]
    [InlineData(2, 0, 2, 0, 3, 12)]
    [InlineData(12, 0, 0, 0, 1, 12)]
    [InlineData(20, 8, 12, 8, 13, 12)]
    [InlineData(-1, 0, 0, 0, 5, 12)]
    [InlineData(99, 0, 4, 0, 5, 12)]
    [InlineData(0, 50, 0, 0, 5, 12)]
    [InlineData(15, 0, 15, 4, 20, 12)]
    public void ClampScroll_keeps_selection_visible(int selectedIn, int scrollIn, int selectedOut, int scrollOut, int count, int pageSize)
    {
        int selected = selectedIn;
        int scroll = scrollIn;

        FuzzyPicker<string>.ClampScroll(count, ref selected, ref scroll, pageSize);

        selected.Should().Be(selectedOut);
        scroll.Should().Be(scrollOut);
    }

    [Fact]
    public void ClampScroll_resets_when_there_are_no_items()
    {
        int selected = 7;
        int scroll = 3;

        FuzzyPicker<string>.ClampScroll(0, ref selected, ref scroll, 12);

        selected.Should().Be(0);
        scroll.Should().Be(0);
    }
}