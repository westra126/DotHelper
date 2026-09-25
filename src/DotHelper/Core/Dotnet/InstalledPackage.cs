namespace DotHelper.Core.Dotnet;

/// <summary>
/// One row of <c>dotnet list &lt;project&gt; package</c>.
/// </summary>
public sealed record InstalledPackage
{
    public required string Id { get; init; }

    /// <summary>
    /// Version requested by the project (may be a range like <c>[6.0, 7.0)</c> or a floating
    /// version like <c>13.*</c>); <c>null</c> for transitive packages, which have no request.
    /// </summary>
    public string? Requested { get; init; }

    /// <summary>Version actually resolved by restore.</summary>
    public required string Resolved { get; init; }

    public required bool IsTransitive { get; init; }
}