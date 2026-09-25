namespace DotHelper.Core.Workspace;

/// <summary>
/// Finds solution/project files under a directory tree, skipping build/output noise.
/// Used by <c>dh sln add</c> (project candidates) and <c>dh list solutions</c>.
/// </summary>
public static class WorkspaceScanner
{
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
        "obj",
        ".git",
        ".vs",
        ".idea",
        "node_modules",
    };

    /// <summary>
    /// Recursively collects <c>*.csproj</c>/<c>*.fsproj</c> under <paramref name="rootDirectory"/>,
    /// skipping <c>bin</c>/<c>obj</c> and other build output folders.
    /// </summary>
    public static IReadOnlyList<string> FindProjects(string rootDirectory, int maxDepth = 8) =>
        Find(rootDirectory, maxDepth, static ext =>
            ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".fsproj", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Recursively collects <c>*.sln</c>/<c>*.slnx</c> under <paramref name="rootDirectory"/>,
    /// skipping <c>bin</c>/<c>obj</c> and other build output folders.
    /// </summary>
    public static IReadOnlyList<string> FindSolutions(string rootDirectory, int maxDepth = 6) =>
        Find(rootDirectory, maxDepth, static ext =>
            ext.Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".slnx", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> Find(
        string rootDirectory,
        int maxDepth,
        Func<string, bool> extensionPredicate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        List<string> found = [];
        Walk(rootDirectory, 0, maxDepth, extensionPredicate, found);
        found.Sort(StringComparer.OrdinalIgnoreCase);
        return found;
    }

    private static void Walk(
        string directory,
        int depth,
        int maxDepth,
        Func<string, bool> extensionPredicate,
        List<string> found)
    {
        if (depth > maxDepth)
        {
            return;
        }

        try
        {
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                if (extensionPredicate(Path.GetExtension(file)))
                {
                    found.Add(Path.GetFullPath(file));
                }
            }

            foreach (string sub in Directory.EnumerateDirectories(directory))
            {
                string name = Path.GetFileName(sub);
                if (SkippedDirectories.Contains(name))
                {
                    continue;
                }

                Walk(sub, depth + 1, maxDepth, extensionPredicate, found);
            }
        }
        catch (IOException)
        {
            // Best-effort discovery.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort discovery.
        }
    }
}