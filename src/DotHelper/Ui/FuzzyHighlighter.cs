namespace DotHelper.Ui;

/// <summary>
/// Pure match-position logic used by <see cref="FuzzyPicker{T}"/> to highlight fzf-style hits.
/// </summary>
public static class FuzzyHighlighter
{
    /// <summary>
    /// Finds the character positions of <paramref name="query"/> inside <paramref name="text"/>
    /// (case-insensitive). Prefers a contiguous substring hit; falls back to a subsequence hit.
    /// Returns an empty list for an empty query or when there is no match at all.
    /// </summary>
    public static IReadOnlyList<int> FindMatchPositions(string? text, string? query)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query))
        {
            return [];
        }

        string haystack = text.ToLowerInvariant();
        string needle = query.Trim().ToLowerInvariant();
        if (needle.Length == 0)
        {
            return [];
        }

        int index = haystack.IndexOf(needle, StringComparison.Ordinal);
        if (index >= 0)
        {
            int[] contiguous = new int[needle.Length];
            for (int i = 0; i < needle.Length; i++)
            {
                contiguous[i] = index + i;
            }

            return contiguous;
        }

        List<int> subsequence = new(needle.Length);
        int cursor = 0;
        foreach (char c in needle)
        {
            while (cursor < haystack.Length && haystack[cursor] != c)
            {
                cursor++;
            }

            if (cursor >= haystack.Length)
            {
                return [];
            }

            subsequence.Add(cursor);
            cursor++;
        }

        return subsequence;
    }
}