using Spectre.Console;

namespace DotHelper.Ui;

/// <summary>
/// The ONE canonical source of the application banner
/// (<c>DotHelper 1.0.0 — asistente para .NET</c>). Every interactive surface — root menu,
/// sub-menus, flow pickers and text/confirm prompts — renders these lines at the very top of
/// its frame by default (see <see cref="FuzzyPickerOptions{T}.Header"/> and
/// <c>Prompts</c>); call sites never duplicate the strings. The header is one compact line so
/// it fits dialogs as well as menus; contextual key hints (Esc back/exit/cancel…) live in each
/// dialog's own hint row.
/// </summary>
public static class AppHeader
{
    public const string ProductName = "DotHelper";

    public const string Tagline = "— asistente para .NET";

    /// <summary>Version shown when the caller does not provide one (assembly version).</summary>
    public static string DefaultVersion { get; } =
        typeof(AppHeader).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>
    /// The canonical banner lines (Spectre markup, trusted). Exactly one line; the version is
    /// injectable for tests. Pass these as <see cref="FuzzyPickerOptions{T}.Header"/> (or prompt
    /// headers) to override the default; pass an empty list to suppress the header entirely.
    /// </summary>
    public static IReadOnlyList<string> Lines(string? version = null) =>
        [
            $"[{Theme.AccentMarkup}]{ProductName}[/] [{Theme.MutedMarkup}]{Markup.Escape(version ?? DefaultVersion)}[/] " +
                $"[{Theme.MutedMarkup}]{Tagline}[/]",
        ];

    /// <summary>Prints the banner lines directly (byte-stable rendering of <see cref="Lines"/>).</summary>
    public static void Render(IAnsiConsole console, string? version = null)
    {
        ArgumentNullException.ThrowIfNull(console);

        foreach (string line in Lines(version))
        {
            console.MarkupLine(line);
        }
    }
}