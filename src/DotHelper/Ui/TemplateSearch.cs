using DotHelper.Core.Dotnet;

namespace DotHelper.Ui;

/// <summary>
/// Fuzzy-search wiring for <see cref="TemplateInfo"/>: field weights per PLAN.md §6c
/// (ShortName &gt; Name &gt; Tags) plus the picker options used by <c>dh list templates</c>.
/// </summary>
public static class TemplateSearch
{
    /// <summary>Searchable fields of a template with their ranking weights.</summary>
    public static IReadOnlyList<WeightedField> Fields(TemplateInfo template)
    {
        ArgumentNullException.ThrowIfNull(template);

        List<WeightedField> fields = new(template.ShortNames.Length + 1 + template.Tags.Length);
        foreach (string shortName in template.ShortNames)
        {
            fields.Add(new WeightedField(shortName, WeightedField.ShortNameWeight));
        }

        fields.Add(new WeightedField(template.Name, WeightedField.NameWeight));

        foreach (string tag in template.Tags)
        {
            fields.Add(new WeightedField(tag, WeightedField.TagWeight));
        }

        return fields;
    }

    /// <summary>Tiebreak: project templates before item templates on equal scores.</summary>
    public static IComparer<TemplateInfo> TypeTiebreak { get; } =
        FuzzyScorer.TypePriority<TemplateInfo>(static t => t.Type);

    /// <summary>Picker options for browsing templates interactively.</summary>
    public static FuzzyPickerOptions<TemplateInfo> PickerOptions(
        string? initialQuery = null,
        EscHint escHint = EscHint.Cancel) => new()
        {
            PrimaryText = static t => t.Name,
            Fields = Fields,
            DetailLines = static t => new[]
            {
            $"Short names: {string.Join(", ", t.ShortNames)}",
            $"Type:        {(t.Type.Length == 0 ? "(none)" : t.Type)}",
            $"Languages:   {(t.Languages.Length == 0 ? "(none)" : string.Join(", ", t.Languages))}",
            $"Author:      {t.Author}",
            $"Tags:        {(t.Tags.Length == 0 ? "(none)" : string.Join(", ", t.Tags))}",
        },
            Title = "templates",
            Tiebreak = TypeTiebreak,
            InitialQuery = initialQuery,
            EscHint = escHint,
        };
}