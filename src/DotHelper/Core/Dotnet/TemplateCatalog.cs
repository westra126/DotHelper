namespace DotHelper.Core.Dotnet;

/// <summary>
/// Enumerates the <c>dotnet new</c> template catalog and caches it in-process (PLAN.md §6b).
/// </summary>
public sealed class TemplateCatalog
{
    /// <summary>Arguments used to list the full template catalog.</summary>
    public static readonly IReadOnlyList<string> ListArgs =
        new[] { "new", "list", "--ignore-constraints", "--columns-all" };

    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    private readonly IDotnetRunner _runner;
    private readonly TimeSpan _ttl;
    private IReadOnlyList<TemplateInfo>? _cache;
    private DateTimeOffset _cachedAtUtc;

    public TemplateCatalog(IDotnetRunner runner, TimeSpan? ttl = null)
    {
        _runner = runner;
        _ttl = ttl ?? DefaultTtl;
    }

    /// <summary>
    /// Returns the template catalog, using an in-process cache (default TTL: 10 minutes).
    /// Only successful runs are cached; a failed <c>dotnet new list</c> raises a friendly
    /// <see cref="InvalidOperationException"/> (Fase 6 review) and is retried next call.
    /// </summary>
    public async Task<IReadOnlyList<TemplateInfo>> GetTemplatesAsync(CancellationToken cancellationToken = default)
    {
        if (_cache is not null && DateTimeOffset.UtcNow - _cachedAtUtc < _ttl)
        {
            return _cache;
        }

        DotnetResult result = await _runner
            .RunAsync(ListArgs, workingDir: null, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            // Never cache failures: the next call must retry the CLI.
            throw new InvalidOperationException(NugetService.DescribeError(result));
        }

        IReadOnlyList<TemplateInfo> templates = TemplateListParser.Parse(result.StdOut);

        _cache = templates;
        _cachedAtUtc = DateTimeOffset.UtcNow;
        return templates;
    }
}