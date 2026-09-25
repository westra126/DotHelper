namespace DotHelper.Core.Dotnet;

/// <summary>
/// Parses the table printed by <c>dotnet list &lt;project&gt; package [--include-transitive]</c>.
/// </summary>
/// <remarks>
/// Locale-agnostic on purpose: headers are localized ("Top-level Package" / "Paquete de nivel
/// superior", "Requested" / "Solicitado"…) and this output has <b>no dashes separator row</b>
/// (verified on SDK 10.0.111), so neither header text nor a separator is consulted. The stable
/// anchors are:
/// <list type="bullet">
/// <item>data rows start with a <c>&gt;</c> marker (identical in every locale);</item>
/// <item>columns start at non-space characters preceded by a run of 2+ spaces — the same
/// offset strategy as <see cref="TemplateListParser"/>, applied per row. Cells never contain
/// 2+ consecutive spaces, while a requested version may contain single spaces (e.g. the range
/// <c>[6.0, 7.0)</c>), so row slicing keeps such values intact.</item>
/// </list>
///
/// Row shapes (empirically, a top-level request is never empty — a <c>PackageReference</c>
/// without a version does not survive restore):
/// <list type="bullet">
/// <item>3 cells → top-level: id, requested, resolved.</item>
/// <item>2 cells → transitive: id, resolved (transitive packages have no request).</item>
/// <item>1 cell → skipped; more than 3 → the middle cells re-join as the requested range.</item>
/// </list>
///
/// The "no packages" case prints a localized message on the framework line and yields no rows.
/// Multi-targeted projects print one table per framework; every row is returned as printed.
/// </remarks>
public static class PackageListParser
{
    /// <summary>
    /// Parses <paramref name="output"/> into installed packages.
    /// Returns an empty list for null/blank output or when no rows are present.
    /// </summary>
    public static IReadOnlyList<InstalledPackage> Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        string[] lines = output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        List<InstalledPackage> rows = [];
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] != '>')
            {
                continue;
            }

            InstalledPackage? package = ParseRow(line);
            if (package is not null)
            {
                rows.Add(package);
            }
        }

        return rows;
    }

    private static InstalledPackage? ParseRow(string line)
    {
        List<int> starts = GetColumnStarts(line);
        if (starts.Count == 0)
        {
            return null;
        }

        string id = Slice(line, starts, 0).TrimStart('>').Trim();
        if (id.Length == 0)
        {
            return null;
        }

        if (starts.Count == 2)
        {
            return new InstalledPackage
            {
                Id = id,
                Requested = null,
                Resolved = Slice(line, starts, 1),
                IsTransitive = true,
            };
        }

        if (starts.Count == 1)
        {
            return null;
        }

        // 3+ cells: everything between the id and the last cell is the requested value,
        // re-joined in case a range was split by its internal 2+ space run.
        List<string> middle = [];
        for (int i = 1; i < starts.Count - 1; i++)
        {
            middle.Add(Slice(line, starts, i));
        }

        return new InstalledPackage
        {
            Id = id,
            Requested = string.Join(" ", middle),
            Resolved = Slice(line, starts, starts.Count - 1),
            IsTransitive = false,
        };
    }

    /// <summary>
    /// Column starts of a line: every non-space character preceded by a run of 2+ spaces
    /// (or sitting at index 0). Single spaces inside a cell (e.g. <c>Top-level Package</c>,
    /// <c>[6.0, 7.0)</c>) do not start a new column.
    /// </summary>
    private static List<int> GetColumnStarts(string line)
    {
        List<int> starts = [];
        int spaceRun = 0;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c is ' ' or '\t')
            {
                spaceRun++;
                continue;
            }

            if (i == 0 || spaceRun >= 2)
            {
                starts.Add(i);
            }

            spaceRun = 0;
        }

        return starts;
    }

    /// <summary>
    /// Slices one column: middle columns end where the next one starts; the last column runs to
    /// end of line. Missing / short rows yield empty strings.
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
}