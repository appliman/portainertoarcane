using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PortainerToArcane;

public sealed partial class BackupReader
{
    public async Task<BackupSnapshot> ReadAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        var fullPath = Path.GetFullPath(archivePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Archive Portainer introuvable.", fullPath);
        }

        var composeById = new Dictionary<int, string>();
        var environmentById = new Dictionary<int, string>();
        var sensitiveEntries = new List<string>();
        string? exportJson = null;
        var customTemplateCount = 0;

        await using var file = File.OpenRead(fullPath);
        await using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        TarEntry? entry;

        while ((entry = await reader.GetNextEntryAsync(copyData: false, cancellationToken)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.EntryType == TarEntryType.Directory || entry.DataStream is null)
            {
                continue;
            }

            var name = NormalizeArchivePath(entry.Name);
            if (IsSensitiveEntry(name))
            {
                sensitiveEntries.Add(name);
            }

            var composeMatch = ComposePathRegex().Match(name);
            if (composeMatch.Success)
            {
                composeById[ParseId(composeMatch)] = await ReadTextAsync(entry.DataStream, cancellationToken);
                continue;
            }

            var environmentMatch = EnvironmentPathRegex().Match(name);
            if (environmentMatch.Success)
            {
                environmentById[ParseId(environmentMatch)] = await ReadTextAsync(entry.DataStream, cancellationToken);
                continue;
            }

            if (CustomTemplateRegex().IsMatch(name))
            {
                customTemplateCount++;
                continue;
            }

            if (name.StartsWith("export-", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                exportJson = await ReadTextAsync(entry.DataStream, cancellationToken);
            }
        }

        if (exportJson is null)
        {
            throw new InvalidDataException("Le backup ne contient aucun export JSON Portainer.");
        }

        using var document = JsonDocument.Parse(exportJson);
        var root = document.RootElement;
        return new BackupSnapshot(
            fullPath,
            ReadSchemaVersion(root),
            ReadEndpoints(root),
            ReadStacks(root, composeById, environmentById),
            customTemplateCount,
            GetArrayLength(root, "users"),
            GetArrayLength(root, "registries"),
            sensitiveEntries.Order(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static IReadOnlyList<EndpointInfo> ReadEndpoints(JsonElement root)
    {
        if (!root.TryGetProperty("endpoints", out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return values.EnumerateArray().Select(item =>
        {
            var snapshot = item.TryGetProperty("Snapshots", out var snapshots) &&
                           snapshots.ValueKind == JsonValueKind.Array &&
                           snapshots.GetArrayLength() > 0
                ? snapshots[0]
                : default;
            return new EndpointInfo(
                GetInt(item, "Id"),
                GetString(item, "Name") ?? $"endpoint-{GetInt(item, "Id")}",
                GetInt(item, "Type"),
                GetString(item, "ContainerEngine") ?? "docker",
                GetInt(item, "Status"),
                GetString(snapshot, "DockerVersion"),
                GetInt(snapshot, "ContainerCount"),
                GetInt(snapshot, "RunningContainerCount"),
                GetInt(snapshot, "StackCount"));
        }).OrderBy(static endpoint => endpoint.Id).ToArray();
    }

    private static IReadOnlyList<StackInfo> ReadStacks(
        JsonElement root,
        IReadOnlyDictionary<int, string> composeById,
        IReadOnlyDictionary<int, string> environmentById)
    {
        if (!root.TryGetProperty("stacks", out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return values.EnumerateArray().Select(item =>
        {
            var id = GetInt(item, "Id");
            var environmentNames = item.TryGetProperty("Env", out var environment) &&
                                   environment.ValueKind == JsonValueKind.Array
                ? environment.EnumerateArray()
                    .Select(static value => GetString(value, "name"))
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Select(static value => value!)
                    .ToArray()
                : [];
            var isGit = item.TryGetProperty("GitConfig", out var git) && git.ValueKind != JsonValueKind.Null;
            return new StackInfo(
                id,
                GetString(item, "Name") ?? $"stack-{id}",
                GetInt(item, "Type"),
                GetInt(item, "EndpointId"),
                GetInt(item, "Status"),
                GetString(item, "EntryPoint") ?? "docker-compose.yml",
                environmentNames,
                composeById.GetValueOrDefault(id),
                environmentById.GetValueOrDefault(id),
                isGit ? "Git" : GetBool(item, "FromAppTemplate") ? "Template" : "Editor/Upload");
        }).OrderBy(static stack => stack.EndpointId)
          .ThenBy(static stack => stack.Name, StringComparer.OrdinalIgnoreCase)
          .ToArray();
    }

    private static string? ReadSchemaVersion(JsonElement root)
    {
        if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        return GetString(version, "SchemaVersion");
    }

    private static int GetArrayLength(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.GetArrayLength()
            : 0;

    private static int ParseId(Match match) =>
        int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

    private static string NormalizeArchivePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static bool IsSensitiveEntry(string name) =>
        name.EndsWith(".key", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("key.pem", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("stack.env", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith("portainer.db", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("export-", StringComparison.OrdinalIgnoreCase);

    private static async Task<string> ReadTextAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static int GetInt(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.TryGetInt32(out var result) ? result : 0;

    private static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool GetBool(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.True;

    [GeneratedRegex(@"^compose/(\d+)/docker-compose\.ya?ml$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ComposePathRegex();

    [GeneratedRegex(@"^compose/(\d+)/stack\.env$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentPathRegex();

    [GeneratedRegex(@"^custom_templates/\d+/docker-compose\.ya?ml$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CustomTemplateRegex();
}
