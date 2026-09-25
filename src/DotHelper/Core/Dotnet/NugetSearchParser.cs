using System.Text.Json;

namespace DotHelper.Core.Dotnet;

/// <summary>
/// Maps package-search JSON to <see cref="NugetPackageInfo"/>. Two real shapes are supported
/// (both captured from live sources, SDK 10.0.111):
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>
/// <c>dotnet package search --format json</c> (schema <c>version: 2</c>):
/// <c>{ version, problems, searchResult: [ { sourceName, packages: [ { id, latestVersion,
/// totalDownloads, owners } ] } ] }</c>. With <c>--verbosity detailed</c> each package also
/// carries <c>description</c> and <c>projectUrl</c>. The CLI never reports <c>verified</c>.
/// <c>owners</c> is a comma-separated string. All sources are concatenated in order.
/// </item>
/// <item>
/// nuget.org HTTP search (<c>https://azuresearch-usnc.nuget.org/query</c>):
/// <c>{ totalHits, data: [ { id, version, description, totalDownloads, verified, owners: [] } ] }</c>
/// — <c>version</c> maps to <see cref="NugetPackageInfo.LatestVersion"/> and the owners array is
/// joined with <c>", "</c>.
/// </item>
/// </list>
/// Unknown fields are ignored; missing optional fields map to <c>null</c>. Malformed JSON is not
/// swallowed: callers decide how to surface it.
/// </remarks>
public static class NugetSearchParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Parses the JSON printed by <c>dotnet package search --format json</c>.</summary>
    public static IReadOnlyList<NugetPackageInfo> ParseDotnetJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        List<NugetPackageInfo> packages = [];
        if (!root.TryGetProperty("searchResult", out JsonElement searchResult) ||
            searchResult.ValueKind != JsonValueKind.Array)
        {
            return packages;
        }

        foreach (JsonElement source in searchResult.EnumerateArray())
        {
            if (!source.TryGetProperty("packages", out JsonElement sourcePackages) ||
                sourcePackages.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement package in sourcePackages.EnumerateArray())
            {
                packages.Add(MapDotnetPackage(package));
            }
        }

        return packages;
    }

    /// <summary>Parses the JSON returned by <c>https://azuresearch-usnc.nuget.org/query</c>.</summary>
    public static IReadOnlyList<NugetPackageInfo> ParseAzureSearchJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        List<NugetPackageInfo> packages = [];
        if (!root.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
        {
            return packages;
        }

        foreach (JsonElement package in data.EnumerateArray())
        {
            packages.Add(MapAzureSearchPackage(package));
        }

        return packages;
    }

    /// <summary>Serializes packages for <c>--json</c> output (camelCase, compact).</summary>
    public static string ToJson(IReadOnlyList<NugetPackageInfo> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        return JsonSerializer.Serialize(packages, Options);
    }

    private static NugetPackageInfo MapDotnetPackage(JsonElement package)
    {
        return new NugetPackageInfo
        {
            Id = GetString(package, "id") ?? string.Empty,
            LatestVersion = GetString(package, "latestVersion") ?? string.Empty,
            Description = GetString(package, "description"),
            TotalDownloads = GetInt64(package, "totalDownloads"),
            Verified = GetBoolean(package, "verified"),
            Owners = GetString(package, "owners"),
        };
    }

    private static NugetPackageInfo MapAzureSearchPackage(JsonElement package)
    {
        return new NugetPackageInfo
        {
            Id = GetString(package, "id") ?? string.Empty,
            LatestVersion = GetString(package, "version") ?? string.Empty,
            Description = GetString(package, "description"),
            TotalDownloads = GetInt64(package, "totalDownloads"),
            Verified = GetBoolean(package, "verified"),
            Owners = JoinOwners(package),
        };
    }

    /// <summary>Owners is a string in the CLI JSON and an array on the HTTP API.</summary>
    private static string? JoinOwners(JsonElement package)
    {
        if (!package.TryGetProperty("owners", out JsonElement owners))
        {
            return null;
        }

        if (owners.ValueKind == JsonValueKind.String)
        {
            return owners.GetString();
        }

        if (owners.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        List<string> names = [];
        foreach (JsonElement owner in owners.EnumerateArray())
        {
            if (owner.ValueKind == JsonValueKind.String && owner.GetString() is { Length: > 0 } name)
            {
                names.Add(name);
            }
        }

        return names.Count == 0 ? null : string.Join(", ", names);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? GetInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : null;

    private static bool? GetBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;
}