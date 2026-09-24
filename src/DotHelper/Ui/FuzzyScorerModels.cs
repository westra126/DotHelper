namespace DotHelper.Ui;

/// <summary>
/// One searchable text field of an item, with its ranking weight (PLAN.md §6c).
/// </summary>
public readonly record struct WeightedField(string Text, double Weight)
{
    public const double ShortNameWeight = 1.0;

    public const double NameWeight = 0.85;

    public const double TagWeight = 0.5;
}

/// <summary>An item with its final weighted ranking score.</summary>
public readonly record struct ScoredItem<T>(T Item, int Score);