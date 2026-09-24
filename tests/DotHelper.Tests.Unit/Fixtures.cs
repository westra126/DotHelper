namespace DotHelper.Tests.Unit;

/// <summary>Resolves fixture files copied to the test output directory.</summary>
internal static class Fixtures
{
    public static string Resolve(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}