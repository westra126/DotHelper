using Raffinert.FuzzySharp;

namespace DotHelper.Ui;

/// <summary>
/// Multi-field fuzzy ranking (PLAN.md §6c). Pure logic, no console dependency.
/// </summary>
/// <remarks>
/// Per-field ratio = <c>max(Fuzz.WeightedRatio, Fuzz.TokenSetRatio)</c> on lowercased text
/// (case-insensitive). <c>WeightedRatio</c> is a strong general-purpose scorer;
/// <c>TokenSetRatio</c> adds robustness when field and query token order/duplication differ
/// (e.g. query <c>api web</c> vs name <c>Web API</c>).
///
/// Ranking score = <c>max_field(ratio(query, field.Text) * field.Weight)</c>
/// with the PLAN.md weights 1.0 / 0.85 / 0.5 (shortName / name / tags).
///
/// Cutoff gates on the best <b>raw</b> field ratio (not the weighted one) so that a strong
/// tag-only match (weight 0.5 → max weighted 50) can still surface under the default cutoff 60;
/// the weights decide the order, the cutoff decides relevance. Documented deviation, see report.
///
/// Ordering is stable (source order preserved on ties); an optional <paramref name="tiebreak"/>
/// further orders equal scores (e.g. project &gt; item via <see cref="TypePriority{T}"/>).
/// An empty query returns every item in original order, untouched.
/// </remarks>
public static class FuzzyScorer
{
    public const int DefaultCutoff = 60;

    /// <summary>
    /// Ranks <paramref name="items"/> against <paramref name="query"/>.
    /// Returns <c>(Item, Score)</c> sorted best-first. Score is the weighted score rounded to int.
    /// </summary>
    public static IReadOnlyList<ScoredItem<T>> Rank<T>(
        string? query,
        IEnumerable<T> items,
        Func<T, IReadOnlyList<WeightedField>> fieldSelector,
        int cutoff = DefaultCutoff,
        IComparer<T>? tiebreak = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(fieldSelector);

        List<T> source = items as List<T> ?? items.ToList();

        if (string.IsNullOrWhiteSpace(query))
        {
            List<ScoredItem<T>> all = new(source.Count);
            foreach (T item in source)
            {
                all.Add(new ScoredItem<T>(item, 0));
            }

            return all;
        }

        string normalizedQuery = query.Trim().ToLowerInvariant();
        List<(T Item, double Weighted, double Raw)> scored = new(source.Count);

        foreach (T item in source)
        {
            double bestWeighted = 0;
            double bestRaw = 0;

            foreach (WeightedField field in fieldSelector(item))
            {
                if (string.IsNullOrEmpty(field.Text))
                {
                    continue;
                }

                double raw = FieldRatio(normalizedQuery, field.Text);
                if (raw > bestRaw)
                {
                    bestRaw = raw;
                }

                double weighted = raw * field.Weight;
                if (weighted > bestWeighted)
                {
                    bestWeighted = weighted;
                }
            }

            if (bestRaw < cutoff)
            {
                continue;
            }

            scored.Add((item, bestWeighted, bestRaw));
        }

        IOrderedEnumerable<(T Item, double Weighted, double Raw)> ordered = scored
            .OrderByDescending(static x => x.Weighted);

        if (tiebreak is not null)
        {
            ordered = ordered.ThenBy(x => x.Item, tiebreak);
        }

        List<ScoredItem<T>> result = new(scored.Count);
        foreach ((T item, double weighted, _) in ordered)
        {
            result.Add(new ScoredItem<T>(item, (int)Math.Round(weighted, MidpointRounding.AwayFromZero)));
        }

        return result;
    }

    /// <summary>
    /// Tiebreak comparer: <c>project</c> first, <c>item</c> last, anything else in between.
    /// </summary>
    public static IComparer<T> TypePriority<T>(Func<T, string> typeSelector)
    {
        ArgumentNullException.ThrowIfNull(typeSelector);

        return Comparer<T>.Create((a, b) =>
        {
            int pa = Priority(typeSelector(a));
            int pb = Priority(typeSelector(b));
            return pa.CompareTo(pb);
        });

        static int Priority(string type) => type.ToLowerInvariant() switch
        {
            "project" => 0,
            "item" => 2,
            _ => 1,
        };
    }

    private static double FieldRatio(string normalizedQuery, string text)
    {
        string normalizedText = text.ToLowerInvariant();
        double weightedRatio = Fuzz.WeightedRatio(normalizedQuery, normalizedText);
        double tokenSetRatio = Fuzz.TokenSetRatio(normalizedQuery, normalizedText);
        return Math.Max(weightedRatio, tokenSetRatio);
    }
}