using Spectre.Console;

namespace DotHelper.Ui;

/// <summary>
/// Small factory of common prompts (name, folder, confirm) for the wizards of Fase 3+.
/// Kept minimal on purpose: only what the planned flows need.
/// </summary>
public static class Prompts
{
    /// <summary>Asks for a non-empty name (project, class, solution…).</summary>
    public static string AskName(IAnsiConsole console, string prompt, string? defaultValue = null)
    {
        ArgumentNullException.ThrowIfNull(console);

        TextPrompt<string> textPrompt = new(prompt);
        if (defaultValue is not null)
        {
            textPrompt = textPrompt.DefaultValue(defaultValue);
        }

        textPrompt.Validate(static value =>
            string.IsNullOrWhiteSpace(value)
                ? ValidationResult.Error("A value is required")
                : ValidationResult.Success());

        return console.Prompt(textPrompt);
    }

    /// <summary>Asks for a folder path (may be empty, meaning "project root").</summary>
    public static string AskFolder(IAnsiConsole console, string prompt, string? defaultValue = null)
    {
        ArgumentNullException.ThrowIfNull(console);

        TextPrompt<string> folderPrompt = new(prompt);
        if (defaultValue is not null)
        {
            folderPrompt = folderPrompt.DefaultValue(defaultValue);
        }

        return console.Prompt(folderPrompt);
    }

    /// <summary>Yes/no confirmation with a default.</summary>
    public static bool Confirm(IAnsiConsole console, string prompt, bool defaultValue = true)
    {
        ArgumentNullException.ThrowIfNull(console);
        return console.Prompt(new ConfirmationPrompt(prompt) { DefaultValue = defaultValue });
    }
}