using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PortainerToArcane;

public sealed partial class ArcaneExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<MigrationManifest> ExportAsync(
        BackupSnapshot snapshot,
        IReadOnlyCollection<StackAssessment> selectedStacks,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        var root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(root);
        var endpointManifests = new List<EndpointManifest>();
        var projects = new List<ExportedProject>();

        foreach (var endpointGroup in selectedStacks.GroupBy(static item => item.Stack.EndpointId).OrderBy(static group => group.Key))
        {
            var endpoint = snapshot.Endpoints.FirstOrDefault(item => item.Id == endpointGroup.Key)
                ?? new EndpointInfo(endpointGroup.Key, $"endpoint-{endpointGroup.Key}", 0, "docker", 0);
            var endpointDirectoryName = $"endpoint-{endpoint.Id}-{SanitizeName(endpoint.Name)}";
            var endpointDirectory = EnsureChildPath(root, endpointDirectoryName);
            Directory.CreateDirectory(endpointDirectory);
            endpointManifests.Add(new(endpoint.Id, endpoint.Name, endpointDirectoryName));

            foreach (var assessment in endpointGroup.OrderBy(static item => item.Stack.Name, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var projectDirectory = EnsureChildPath(endpointDirectory, SanitizeName(assessment.Stack.Name));
                Directory.CreateDirectory(projectDirectory);

                var composePath = Path.Combine(projectDirectory, "compose.yaml");
                await File.WriteAllTextAsync(
                    composePath,
                    NormalizeText(assessment.Stack.ComposeContent!),
                    new UTF8Encoding(false),
                    cancellationToken);

                string? environmentPath = null;
                if (!string.IsNullOrWhiteSpace(assessment.Stack.EnvironmentContent))
                {
                    environmentPath = Path.Combine(projectDirectory, ".env");
                    await File.WriteAllTextAsync(
                        environmentPath,
                        NormalizeText(assessment.Stack.EnvironmentContent),
                        new UTF8Encoding(false),
                        cancellationToken);
                    RestrictEnvironmentFile(environmentPath);
                }

                var metadata = new
                {
                    source = "Portainer",
                    endpointId = endpoint.Id,
                    endpointName = endpoint.Name,
                    stackId = assessment.Stack.Id,
                    stackName = assessment.Stack.Name,
                    risk = assessment.Risk.ToString(),
                    findings = assessment.Findings.Select(static finding => new
                    {
                        finding.Code,
                        finding.Message,
                        severity = finding.Severity.ToString()
                    })
                };
                var metadataPath = Path.Combine(projectDirectory, "migration.json");
                await File.WriteAllTextAsync(
                    metadataPath,
                    JsonSerializer.Serialize(metadata, JsonOptions),
                    new UTF8Encoding(false),
                    cancellationToken);

                projects.Add(new(
                    endpoint.Id,
                    assessment.Stack.Id,
                    assessment.Stack.Name,
                    Path.GetRelativePath(root, projectDirectory),
                    Path.GetRelativePath(root, composePath),
                    environmentPath is null ? null : Path.GetRelativePath(root, environmentPath),
                    assessment.Risk));
            }
        }

        var manifest = new MigrationManifest(
            DateTimeOffset.UtcNow,
            snapshot.ArchivePath,
            snapshot.SchemaVersion,
            endpointManifests,
            projects);
        await File.WriteAllTextAsync(
            Path.Combine(root, "migration-manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOptions),
            new UTF8Encoding(false),
            cancellationToken);
        await WriteInstructionsAsync(root, manifest, cancellationToken);
        return manifest;
    }

    public static string SanitizeName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = UnsafeNameRegex().Replace(value.Trim().ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "project" : normalized;
    }

    private static string EnsureChildPath(string parent, string childName)
    {
        var parentPath = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var childPath = Path.GetFullPath(Path.Combine(parentPath, childName));
        if (!childPath.StartsWith(parentPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Chemin de projet invalide.");
        }
        return childPath;
    }

    private static string NormalizeText(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static void RestrictEnvironmentFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static async Task WriteInstructionsAsync(
        string root,
        MigrationManifest manifest,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>
        {
            "# Export Portainer vers Arcane",
            string.Empty,
            "Copier chaque dossier endpoint vers le Projects Directory de l'environnement Arcane correspondant.",
            "Le chemin hôte doit être monté au même chemin dans le conteneur Arcane.",
            string.Empty
        };
        lines.AddRange(manifest.Endpoints.Select(endpoint =>
            $"- Endpoint {endpoint.Id} - {endpoint.Name}: {endpoint.Directory}"));
        lines.Add(string.Empty);
        lines.Add("Attention : les fichiers .env contiennent des secrets et ne doivent jamais être ajoutés à Git.");
        await File.WriteAllLinesAsync(Path.Combine(root, "README.md"), lines, new UTF8Encoding(false), cancellationToken);
    }

    [GeneratedRegex(@"[^a-z0-9._-]+", RegexOptions.CultureInvariant)]
    private static partial Regex UnsafeNameRegex();
}
