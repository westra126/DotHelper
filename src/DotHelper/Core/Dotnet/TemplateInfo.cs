namespace DotHelper.Core.Dotnet;

/// <summary>
/// One row of <c>dotnet new list --ignore-constraints --columns-all</c>.
/// </summary>
public sealed record TemplateInfo
{
    public required string Name { get; init; }

    public required string[] ShortNames { get; init; }

    public required string[] Languages { get; init; }

    /// <summary>Template type: <c>project</c>, <c>item</c>, <c>solution</c>, or empty.</summary>
    public required string Type { get; init; }

    public required string Author { get; init; }

    public required string[] Tags { get; init; }
}