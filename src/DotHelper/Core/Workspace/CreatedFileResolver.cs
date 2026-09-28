namespace DotHelper.Core.Workspace;

/// <summary>
/// Resolves the file a <c>dotnet new</c> invocation created, without glob semantics
/// (Fase 6 review: never treat a user name as a search pattern).
/// </summary>
public static class CreatedFileResolver
{
    /// <summary>Project extensions in deterministic lookup order.</summary>
    private static readonly string[] ProjectExtensions = [".csproj", ".fsproj", ".vbproj"];

    /// <summary>
    /// Finds the created project file <c>&lt;name&gt;.*proj</c> under
    /// <paramref name="directory"/> (csproj → fsproj → vbproj, deterministic). Returns
    /// <c>null</c> when none exists.
    /// </summary>
    public static string? FindProjectFile(string directory, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        foreach (string extension in ProjectExtensions)
        {
            string candidate = Path.Combine(directory, name + extension);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds files whose base name equals <paramref name="name"/> exactly (no wildcard
    /// semantics), in ordinal order. Returns <c>null</c> when there is no match.
    /// </summary>
    public static string? FindByBaseName(string directory, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            foreach (string file in Directory.EnumerateFiles(directory).OrderBy(static f => f, StringComparer.Ordinal))
            {
                if (string.Equals(Path.GetFileNameWithoutExtension(file), name, StringComparison.Ordinal))
                {
                    return file;
                }
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }
}