using Spectre.Console;

namespace DotHelper.Ui;

/// <summary>
/// Shared colors, symbols and styles so every screen looks consistent.
/// Intentionally small (Fase 2); grows with theme polish in Fase 6.
/// </summary>
public static class Theme
{
    public const string SelectionSymbol = "❯";

    public const string PromptSymbol = "?";

    /// <summary>Inline Spectre markup tag used to highlight matched characters.</summary>
    public const string HighlightMarkup = "bold yellow";

    public const string SelectionMarkup = "bold green";

    public const string MutedMarkup = "grey";

    public const string AccentMarkup = "bold cyan";

    public static readonly Color Accent = Color.Cyan1;

    public static readonly Color Selection = Color.Green;

    public static readonly Color Highlight = Color.Yellow;

    public static readonly Color Muted = Color.Grey;

    public static Style HighlightStyle => new(Highlight, decoration: Decoration.Bold);

    public static Style SelectionStyle => new(Selection, decoration: Decoration.Bold);

    public static Style MutedStyle => new(Muted);

    public static Style AccentStyle => new(Accent, decoration: Decoration.Bold);
}