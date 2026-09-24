namespace DotHelper.Core.Dotnet;

/// <summary>
/// Parses the fixed-width table printed by <c>dotnet new list --ignore-constraints --columns-all</c>.
/// Locale-agnostic on purpose: column offsets are derived from the dashes separator row,
/// never from header text (headers are localized, e.g. "Template Name" / "Nombre de la plantilla").
/// </summary>
public static class TemplateListParser
{
    private const int ColumnName = 0;
    private const int ColumnShortNames = 1;
    private const int ColumnLanguages = 2;
    private const int ColumnType = 3;
    private const int ColumnAuthor = 4;
    private const int ColumnTags = 5;

    /// <summary>
    /// Parses <paramref name="output"/> into templates.
    /// Returns an empty list when there is no dashes separator row (including the
    /// "no templates matched" case, which prints to stderr and leaves stdout empty).
    /// </summary>
    public static IReadOnlyList<TemplateInfo> Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        string[] lines = output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        int separatorIndex = FindSeparatorLine(lines);
        if (separatorIndex < 0)
        {
            return [];
        }

        List<int> starts = GetColumnStarts(lines[separatorIndex]);
        if (starts.Count == 0)
        {
            return [];
        }

        List<TemplateInfo> templates = [];
        for (int i = separatorIndex + 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            templates.Add(ParseRow(line, starts));
        }

        return templates;
    }

    private static int FindSeparatorLine(string[] lines)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            if (IsSeparatorLine(lines[i]))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// A separator row is made only of <c>-</c> runs and whitespace (spaces sit between columns).
    /// No English/locale text is consulted.
    /// </summary>
    private static bool IsSeparatorLine(string line)
    {
        bool sawDash = false;
        foreach (char c in line)
        {
            if (c == '-')
            {
                sawDash = true;
            }
            else if (c is not (' ' or '\t' or '\r'))
            {
                return false;
            }
        }

        return sawDash;
    }

    /// <summary>
    /// Derives column offsets from the dashes row: each run of <c>-</c> starts one column.
    /// This mirrors how the CLI pads the header and the data rows, so slicing by these
    /// offsets works for any locale and for names containing spaces.
    /// </summary>
    private static List<int> GetColumnStarts(string separatorLine)
    {
        List<int> starts = [];
        int i = 0;
        while (i < separatorLine.Length)
        {
            if (separatorLine[i] == '-')
            {
                starts.Add(i);
                while (i < separatorLine.Length && separatorLine[i] == '-')
                {
                    i++;
                }
            }
            else
            {
                i++;
            }
        }

        return starts;
    }

    private static TemplateInfo ParseRow(string line, List<int> starts)
    {
        string name = Slice(line, starts, ColumnName);
        string shortNames = Slice(line, starts, ColumnShortNames);
        string languages = Slice(line, starts, ColumnLanguages);
        string type = Slice(line, starts, ColumnType);
        string author = Slice(line, starts, ColumnAuthor);
        string tags = Slice(line, starts, ColumnTags);

        return new TemplateInfo
        {
            Name = name,
            ShortNames = SplitBy(shortNames, ','),
            Languages = ParseLanguages(languages),
            Type = type,
            Author = author,
            Tags = SplitBy(tags, '/'),
        };
    }

    /// <summary>
    /// Slices one column. Middle columns end where the next one starts (so the
    /// inter-column padding is trimmed away); the last column runs to end of line.
    /// Missing / short rows yield empty strings.
    /// </summary>
    private static string Slice(string line, List<int> starts, int index)
    {
        if (index >= starts.Count)
        {
            return string.Empty;
        }

        int start = starts[index];
        if (start >= line.Length)
        {
            return string.Empty;
        }

        int end = index + 1 < starts.Count ? starts[index + 1] : line.Length;
        if (end > line.Length)
        {
            end = line.Length;
        }

        return line[start..end].Trim();
    }

    private static string[] SplitBy(string raw, char separator)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw.Split(separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Languages are comma-separated and optionally bracketed: <c>[C#],F#</c> → <c>C#</c>, <c>F#</c>.
    /// </summary>
    private static string[] ParseLanguages(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(static s => s.Trim().TrimStart('[').TrimEnd(']').Trim())
            .Where(static s => s.Length > 0)
            .ToArray();
    }
}