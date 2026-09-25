using System.Globalization;
using System.Text.Json;

namespace DotHelper.Core.Dotnet;

/// <summary>
/// NuGet operations wrapping <c>dotnet package search</c>, <c>dotnet list package</c>,
/// <c>dotnet add package</c> and <c>dotnet remove package</c> (PLAN.md §4 NugetService).
/// </summary>
/// <remarks>
/// Search runs through the CLI first. Only when the <c>package search</c> subcommand does not
/// exist (older SDKs answer "Could not execute because the specified command or file was not
/// found") does the service fall back to the nuget.org HTTP search endpoint
/// <c>https://azuresearch-usnc.nuget.org/query</c>. The fallback is never used to enrich CLI
/// results, so <see cref="NugetPackageInfo.Verified"/> stays <c>null</c> on the CLI path —
/// the CLI JSON simply does not report that badge (empirical, SDK 10.0.111).
/// </remarks>
public sealed class NugetService
{
    /// <summary>nuget.org search endpoint used only as fallback when the CLI subcommand is missing.</summary>
    public const string AzureSearchEndpoint = "https://azuresearch-usnc.nuget.org/query";

    private static readonly HttpClient SharedSearchClient = new();

    private readonly IDotnetRunner _runner;
    private readonly HttpClient? _searchHttpClient;

    public NugetService(IDotnetRunner runner, HttpClient? searchHttpClient = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _searchHttpClient = searchHttpClient;
    }

    /// <summary>
    /// <c>dotnet package search &lt;query&gt; --take &lt;take&gt; --format json --verbosity detailed</c>.
    /// <c>--verbosity detailed</c> is required for <c>description</c>/<c>projectUrl</c>; the JSON
    /// stays clean on stdout (empirical). Falls back to the HTTP endpoint when the subcommand is
    /// missing; other failures raise a friendly <see cref="InvalidOperationException"/>.
    /// </summary>
    public async Task<IReadOnlyList<NugetPackageInfo>> SearchAsync(
        string? query,
        int take,
        bool prerelease = false,
        CancellationToken cancellationToken = default)
    {
        if (take < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(take), take, "take must be greater than zero.");
        }

        DotnetResult result = await _runner
            .RunAsync(BuildSearchArgs(query, take, prerelease), workingDir: null, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode == 0)
        {
            return ParseJsonOrThrow(() => NugetSearchParser.ParseDotnetJson(result.StdOut), result);
        }

        if (IsMissingSearchSubcommand(result))
        {
            return await SearchViaHttpAsync(query, take, prerelease, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException(DescribeError(result));
    }

    /// <summary>
    /// <c>dotnet list &lt;project&gt; package [--include-transitive]</c> parsed into
    /// <see cref="InstalledPackage"/> rows. Throws a friendly
    /// <see cref="InvalidOperationException"/> when the command fails (e.g. project not found).
    /// </summary>
    public async Task<IReadOnlyList<InstalledPackage>> ListAsync(
        string projectPath,
        bool includeTransitive = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        DotnetResult result = await _runner
            .RunAsync(BuildListArgs(projectPath, includeTransitive), workingDir: null, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(DescribeError(result));
        }

        return PackageListParser.Parse(result.StdOut);
    }

    /// <summary>
    /// <c>dotnet add &lt;project&gt; package &lt;id&gt; [--version &lt;version&gt;]</c>.
    /// Without a version the CLI resolves the latest stable version. Non-zero exits are reported
    /// through the result; callers surface <see cref="DescribeError"/>.
    /// </summary>
    public Task<DotnetResult> AddAsync(
        string projectPath,
        string packageId,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        return _runner.RunAsync(
            BuildAddArgs(projectPath, packageId, version),
            workingDir: null,
            cancellationToken: cancellationToken);
    }

    /// <summary><c>dotnet remove &lt;project&gt; package &lt;id&gt;</c>.</summary>
    public Task<DotnetResult> RemoveAsync(
        string projectPath,
        string packageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        return _runner.RunAsync(
            BuildRemoveArgs(projectPath, packageId),
            workingDir: null,
            cancellationToken: cancellationToken);
    }

    // ---- pure argument builders (kept separate so the exact CLI surface is unit-testable) ----

    /// <summary>
    /// <c>package search [&lt;query&gt;] --take &lt;take&gt; --format json --verbosity detailed [--prerelease]</c>.
    /// The term is positional and omitted when empty (the CLI then lists top packages).
    /// </summary>
    public static IReadOnlyList<string> BuildSearchArgs(string? query, int take, bool prerelease)
    {
        if (take < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(take), take, "take must be greater than zero.");
        }

        List<string> args = new() { "package", "search" };
        if (!string.IsNullOrWhiteSpace(query))
        {
            args.Add(query.Trim());
        }

        args.Add("--take");
        args.Add(take.ToString(CultureInfo.InvariantCulture));
        args.Add("--format");
        args.Add("json");

        // 'detailed' adds description/projectUrl to each package (empirical, SDK 10).
        args.Add("--verbosity");
        args.Add("detailed");

        if (prerelease)
        {
            args.Add("--prerelease");
        }

        return args;
    }

    /// <summary><c>list &lt;project&gt; package [--include-transitive]</c>.</summary>
    public static IReadOnlyList<string> BuildListArgs(string projectPath, bool includeTransitive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        List<string> args = new() { "list", projectPath, "package" };
        if (includeTransitive)
        {
            args.Add("--include-transitive");
        }

        return args;
    }

    /// <summary><c>add &lt;project&gt; package &lt;id&gt; [--version &lt;version&gt;]</c>.</summary>
    public static IReadOnlyList<string> BuildAddArgs(string projectPath, string packageId, string? version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        List<string> args = new() { "add", projectPath, "package", packageId.Trim() };
        if (!string.IsNullOrWhiteSpace(version))
        {
            args.Add("--version");
            args.Add(version.Trim());
        }

        return args;
    }

    /// <summary><c>remove &lt;project&gt; package &lt;id&gt;</c>.</summary>
    public static IReadOnlyList<string> BuildRemoveArgs(string projectPath, string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        return new[] { "remove", projectPath, "package", packageId.Trim() };
    }

    /// <summary>
    /// Builds the fallback URI for <see cref="AzureSearchEndpoint"/>.
    /// </summary>
    public static string BuildAzureSearchUri(string? query, int take, bool prerelease)
    {
        if (take < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(take), take, "take must be greater than zero.");
        }

        return AzureSearchEndpoint +
            "?q=" + Uri.EscapeDataString(query ?? string.Empty) +
            "&take=" + take.ToString(CultureInfo.InvariantCulture) +
            "&prerelease=" + (prerelease ? "true" : "false");
    }

    /// <summary>
    /// True when the CLI rejected the invocation because <c>dotnet package search</c> does not
    /// exist on this SDK (older SDKs only). Anything else (bad flags, network, auth) is a real
    /// error and must not silently fall back to the HTTP endpoint.
    /// </summary>
    public static bool IsMissingSearchSubcommand(DotnetResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.ExitCode == 0)
        {
            return false;
        }

        return ContainsAny(result.StdErr + "\n" + result.StdOut, MissingSubcommandMarkers);
    }

    /// <summary>
    /// Extracts a single friendly error line from a failed <c>dotnet</c> invocation, without any
    /// stack trace: prefers <c>error:</c> lines (NuGet's own wording, e.g. "does not contain any
    /// PackageReference"), then MSBuild <c>: error</c> lines, then the first informative line,
    /// falling back to the exit code.
    /// </summary>
    public static string DescribeError(DotnetResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        foreach (string line in EnumerateLines(result))
        {
            if (line.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
            {
                string message = line["error:".Length..].Trim();
                if (message.Length > 0)
                {
                    return message;
                }
            }
        }

        foreach (string line in EnumerateLines(result))
        {
            int marker = line.IndexOf(": error ", StringComparison.OrdinalIgnoreCase);
            if (marker >= 0)
            {
                string message = line[(marker + ": error ".Length)..].Trim();
                if (message.Length > 0)
                {
                    return message;
                }
            }
        }

        foreach (string line in EnumerateLines(result))
        {
            if (line.Length > 0 && !IsNoise(line))
            {
                return line;
            }
        }

        return $"dotnet exited with code {result.ExitCode}";
    }

    private static readonly string[] MissingSubcommandMarkers =
    {
        "Could not execute because the specified command or file was not found",
        "Unrecognized command or argument",
    };

    private static bool ContainsAny(string haystack, string[] needles)
    {
        foreach (string needle in needles)
        {
            if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateLines(DotnetResult result)
    {
        foreach (string raw in (result.StdErr + "\n" + result.StdOut).Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length > 0)
            {
                yield return line;
            }
        }
    }

    /// <summary>Progress chatter that never describes the failure.</summary>
    private static bool IsNoise(string line) =>
        line.StartsWith("info", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("log", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("warning", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Determining", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Restored", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("All projects", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<NugetPackageInfo> ParseJsonOrThrow(
        Func<IReadOnlyList<NugetPackageInfo>> parse,
        DotnetResult result)
    {
        try
        {
            return parse();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Unexpected output from 'dotnet {ShortCommand(result)}': {ex.Message}", ex);
        }
    }

    private static string ShortCommand(DotnetResult result)
    {
        string line = result.CommandLine;
        int cut = line.IndexOf(" --", StringComparison.Ordinal);
        return (cut >= 0 ? line[..cut] : line).Replace("dotnet ", string.Empty, StringComparison.Ordinal).Trim();
    }

    private async Task<IReadOnlyList<NugetPackageInfo>> SearchViaHttpAsync(
        string? query,
        int take,
        bool prerelease,
        CancellationToken cancellationToken)
    {
        HttpClient client = _searchHttpClient ?? SharedSearchClient;
        string uri = BuildAzureSearchUri(query, take, prerelease);

        string json;
        try
        {
            using HttpResponseMessage response = await client
                .GetAsync(uri, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"NuGet search failed: {ex.Message}", ex);
        }

        try
        {
            return NugetSearchParser.ParseAzureSearchJson(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Unexpected response from {AzureSearchEndpoint}: {ex.Message}", ex);
        }
    }
}