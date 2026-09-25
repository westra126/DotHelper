namespace DotHelper.Core.Dotnet;

/// <summary>
/// One search hit from <c>dotnet package search</c> (or the nuget.org HTTP fallback).
/// Field availability depends on the source (see <see cref="NugetSearchParser"/>):
/// the CLI JSON never carries <see cref="Verified"/> and only prints
/// <see cref="Description"/> with <c>--verbosity detailed</c>.
/// </summary>
public sealed record NugetPackageInfo
{
    public required string Id { get; init; }

    /// <summary>Latest stable version (or latest prerelease when searching with <c>--prerelease</c>).</summary>
    public required string LatestVersion { get; init; }

    public string? Description { get; init; }

    public long? TotalDownloads { get; init; }

    /// <summary>Reserved-prefix badge on nuget.org; <c>null</c> when the source does not report it.</summary>
    public bool? Verified { get; init; }

    /// <summary>Comma-separated owner names (joined from the array on the HTTP fallback).</summary>
    public string? Owners { get; init; }
}