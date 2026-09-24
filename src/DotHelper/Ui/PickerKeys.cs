namespace DotHelper.Ui;

/// <summary>Key actions understood by <see cref="FuzzyPicker{T}"/> (PLAN.md §5.1).</summary>
public enum PickerAction
{
    None,
    MoveUp,
    MoveDown,
    Select,
    Cancel,
    CancelProcess,
    ToggleDetail,
    Backspace,
    Insert,
}

/// <summary>A mapped key press; <see cref="Character"/> is set only for <see cref="PickerAction.Insert"/>.</summary>
public readonly record struct PickerKeyEvent(PickerAction Action, char Character)
{
    /// <summary>Maps a raw console key to a picker action. Pure and fully testable.</summary>
    public static PickerKeyEvent FromConsoleKeyInfo(ConsoleKeyInfo keyInfo)
    {
        if (keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control) && keyInfo.Key == ConsoleKey.C)
        {
            return new PickerKeyEvent(PickerAction.CancelProcess, '\0');
        }

        return keyInfo.Key switch
        {
            ConsoleKey.UpArrow => new PickerKeyEvent(PickerAction.MoveUp, '\0'),
            ConsoleKey.DownArrow => new PickerKeyEvent(PickerAction.MoveDown, '\0'),
            ConsoleKey.Enter => new PickerKeyEvent(PickerAction.Select, '\0'),
            ConsoleKey.Escape => new PickerKeyEvent(PickerAction.Cancel, '\0'),
            ConsoleKey.Tab => new PickerKeyEvent(PickerAction.ToggleDetail, '\0'),
            ConsoleKey.Backspace => new PickerKeyEvent(PickerAction.Backspace, '\0'),
            _ when !char.IsControl(keyInfo.KeyChar) => new PickerKeyEvent(PickerAction.Insert, keyInfo.KeyChar),
            _ => new PickerKeyEvent(PickerAction.None, '\0'),
        };
    }
}