namespace DotHelper.Core.Dotnet;

/// <summary>
/// Parses the output of <c>dotnet sln &lt;sln&gt; list</c>.
/// Locale-agnostic: column offsets come from the dashes row (same strategy as
/// <see cref="TemplateListParser"/>); localized preamble ("Project(s)" / "Proyecto(s)"/
/// "No projects found…") is never matched. No dashes row → empty list.
/// </summary>
public static class SlnListParser
{
    /// <summary>Returns project paths exactly as printed (relative to the solution directory).</summary>
    public static IReadOnlyList<string> Parse(string? output)
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

        List<string> projects = [];
        for (int i = separatorIndex + 1; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length > 0)
            {
                projects.Add(line);
            }
        }

        return projects;
    }

    private static int FindSeparatorLine(string[] lines)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            bool sawDash = false;
            bool other = false;
            foreach (char c in line)
            {
                if (c == '-')
                {
                    sawDash = true;
                }
                else if (c is not (' ' or '\t' or '\r'))
                {
                    other = true;
                    break;
                }
            }

            if (sawDash && !other)
            {
                return i;
            }
        }

        return -1;
    }
}