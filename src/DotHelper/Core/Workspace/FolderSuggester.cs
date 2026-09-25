namespace DotHelper.Core.Workspace;

/// <summary>
/// Pure folder-suggestion rules for <c>dh new project</c> (PLAN.md §5.2: <c>src/</c>/<c>tests/</c>
/// suggested). Kept UI-free so it is unit-testable.
/// </summary>
public static class FolderSuggester
{
    /// <summary>
    /// Suggests the parent folder of a new project:
    /// test templates → <c>tests</c>; anything else with a solution → <c>src</c>; otherwise "" (cwd).
    /// </summary>
    public static string SuggestProjectFolder(bool hasSolution, string templateShortName)
    {
        if (LooksLikeTestTemplate(templateShortName))
        {
            return "tests";
        }

        return hasSolution ? "src" : string.Empty;
    }

    /// <summary>True for xunit/nunit/mstest/test templates.</summary>
    public static bool LooksLikeTestTemplate(string templateShortName)
    {
        if (string.IsNullOrEmpty(templateShortName))
        {
            return false;
        }

        string s = templateShortName.ToLowerInvariant();
        return s.Contains("xunit", StringComparison.Ordinal) ||
               s.Contains("nunit", StringComparison.Ordinal) ||
               s.Contains("mstest", StringComparison.Ordinal) ||
               s.Contains("test", StringComparison.Ordinal);
    }
}