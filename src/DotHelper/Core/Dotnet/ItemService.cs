namespace DotHelper.Core.Dotnet;

/// <summary>
/// Item (class, record, interface…) creation wrapping <c>dotnet new</c> (PLAN.md §4 ItemService).
/// </summary>
/// <remarks>
/// Empirical finding (SDK 10): item templates carry a "created inside the project" constraint.
/// <c>--project</c> is <b>context-only</b> (project capabilities); it does <b>not</b> change the
/// output location, which is always <c>-o</c> (or cwd). We therefore always target
/// <c>-o &lt;projectDir&gt;/&lt;subdir&gt;</c>, which both lands the file where PLAN.md §5.3 expects it
/// and satisfies the constraint naturally. <c>--project</c> is still forwarded when the project
/// file is known, as documentation of intent (harmless).
/// </remarks>
public sealed class ItemService
{
    private readonly IDotnetRunner _runner;

    public ItemService(IDotnetRunner runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    /// <summary>
    /// <c>dotnet new &lt;shortName&gt; -n &lt;name&gt; -o &lt;projectDir&gt;/&lt;outputSubdir&gt;</c>.
    /// <paramref name="outputSubdir"/> is relative to the project directory ("" = project root).
    /// </summary>
    public Task<DotnetResult> CreateAsync(
        string templateShortName,
        string name,
        string projectDirectory,
        string? outputSubdir,
        string? projectFilePath = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateShortName);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);

        string output = string.IsNullOrWhiteSpace(outputSubdir)
            ? projectDirectory
            : Path.Combine(projectDirectory, outputSubdir);

        List<string> args = new() { "new", templateShortName, "-n", name, "-o", output };
        if (!string.IsNullOrWhiteSpace(projectFilePath))
        {
            args.Add("--project");
            args.Add(projectFilePath);
        }

        return _runner.RunAsync(args, workingDir: null, cancellationToken: cancellationToken);
    }
}