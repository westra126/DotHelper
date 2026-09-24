namespace DotHelper.Core.Workspace;

/// <summary>
/// A solution or project file discovered while walking up the directory tree.
/// <see cref="Kind"/> is the extension without the dot: <c>sln</c>, <c>slnx</c>, <c>csproj</c>, <c>fsproj</c>.
/// </summary>
public sealed record WorkspaceArtifact
{
    public required string FullPath { get; init; }

    public required string Kind { get; init; }
}

/// <summary>
/// Result of a workspace lookup: the nearest artifact plus everything found on the way up.
/// </summary>
public sealed record WorkspaceLookupResult
{
    public WorkspaceArtifact? Closest { get; init; }

    public IReadOnlyList<WorkspaceArtifact> All { get; init; } = [];
}