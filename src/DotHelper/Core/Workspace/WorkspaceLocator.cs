namespace DotHelper.Core.Workspace;

/// <summary>
/// Finds solution/project files by walking up from a directory (PLAN.md §6d).
/// UI-free by design; the fuzzy picker for multiple hits lands in Fase 2.
/// </summary>
/// <remarks>
/// Walk rules:
/// <list type="bullet">
/// <item>Matches are collected at every level, nearest first.</item>
/// <item>Walking stops (inclusive) at the first directory containing a <c>.sln</c>/<c>.slnx</c>
/// — the solution is the workspace root.</item>
/// <item>Walking also stops (inclusive) at <c>boundaryDirectory</c> when provided.
/// Tests use this to stay isolated from unrelated files elsewhere on disk.</item>
/// <item>Otherwise the walk ends at the filesystem root.</item>
/// </list>
/// Within one directory, ordering is <c>sln</c> &gt; <c>slnx</c> &gt; <c>csproj</c> &gt; <c>fsproj</c>,
/// then by file name — so <see cref="WorkspaceLookupResult.Closest"/> is deterministic.
/// </remarks>
public static class WorkspaceLocator
{
    private static readonly string[] KindPriority = new[] { "sln", "slnx", "csproj", "fsproj" };

    public static WorkspaceLookupResult Locate(string? startDirectory, string? boundaryDirectory = null)
    {
        string? current = NormalizeDirectory(startDirectory ?? Environment.CurrentDirectory);
        string? boundary = NormalizeDirectory(boundaryDirectory);
        if (current is null)
        {
            return new WorkspaceLookupResult();
        }

        List<WorkspaceArtifact> all = [];

        while (current is not null)
        {
            List<WorkspaceArtifact> found = FindInDirectory(current);
            all.AddRange(found);

            bool hitSolution = found.Exists(static a => a.Kind is "sln" or "slnx");
            if (hitSolution)
            {
                break;
            }

            if (boundary is not null && PathsEqual(current, boundary))
            {
                break;
            }

            current = GetParentDirectory(current);
        }

        return new WorkspaceLookupResult
        {
            Closest = all.Count > 0 ? all[0] : null,
            All = all,
        };
    }

    private static List<WorkspaceArtifact> FindInDirectory(string directory)
    {
        List<WorkspaceArtifact> found = [];
        if (string.IsNullOrWhiteSpace(directory))
        {
            return found;
        }

        try
        {
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                string? kind = Path.GetExtension(file).ToLowerInvariant() switch
                {
                    ".sln" => "sln",
                    ".slnx" => "slnx",
                    ".csproj" => "csproj",
                    ".fsproj" => "fsproj",
                    _ => null,
                };

                if (kind is not null)
                {
                    found.Add(new WorkspaceArtifact { FullPath = file, Kind = kind });
                }
            }
        }
        catch (IOException)
        {
            // Unreadable directory: treat as empty (best-effort discovery).
        }
        catch (UnauthorizedAccessException)
        {
            // Unreadable directory: treat as empty (best-effort discovery).
        }

        found.Sort(static (a, b) =>
        {
            int pa = Array.IndexOf(KindPriority, a.Kind);
            int pb = Array.IndexOf(KindPriority, b.Kind);
            int cmp = pa.CompareTo(pb);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.FullPath, b.FullPath);
        });

        return found;
    }

    private static string? NormalizeDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            string full = Path.GetFullPath(path);
            string trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // Root "/" trims to "" — keep the untrimmed form so callers can keep walking/stopping.
            return trimmed.Length == 0 ? full : trimmed;
        }
        catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string? GetParentDirectory(string directory)
    {
        try
        {
            DirectoryInfo? parent = Directory.GetParent(directory);
            if (parent is null)
            {
                return null;
            }

            string trimmed = parent.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // Reached the filesystem root: stop the walk.
            return trimmed.Length == 0 ? null : trimmed;
        }
        catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Path comparison for the walk boundary: ordinal everywhere, case-insensitive on Windows
    /// where paths are case-insensitive too.
    /// </summary>
    internal static bool PathsEqual(string a, string b) =>
        string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}