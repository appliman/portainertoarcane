namespace PortainerToArcane;

public sealed record EndpointInfo(
    int Id,
    string Name,
    int Type,
    string ContainerEngine,
    int Status,
    string? DockerVersion = null,
    int ContainerCount = 0,
    int RunningContainerCount = 0,
    int StackCount = 0);

public sealed record StackInfo(
    int Id,
    string Name,
    int Type,
    int EndpointId,
    int Status,
    string EntryPoint,
    IReadOnlyList<string> EnvironmentVariableNames,
    string? ComposeContent,
    string? EnvironmentContent,
    string Source);

public sealed record BackupSnapshot(
    string ArchivePath,
    string? SchemaVersion,
    IReadOnlyList<EndpointInfo> Endpoints,
    IReadOnlyList<StackInfo> Stacks,
    int CustomTemplateCount,
    int UserCount,
    int RegistryCount,
    IReadOnlyList<string> SensitiveEntries);

public enum MigrationRisk
{
    Easy,
    Moderate,
    High
}

public sealed record PreflightFinding(string Code, string Message, MigrationRisk Severity);

public sealed record StackAssessment(
    StackInfo Stack,
    MigrationRisk Risk,
    IReadOnlyList<PreflightFinding> Findings);

public sealed record ExportedProject(
    int EndpointId,
    int StackId,
    string StackName,
    string Directory,
    string ComposePath,
    string? EnvironmentPath,
    MigrationRisk Risk);

public sealed record EndpointManifest(int Id, string Name, string Directory);

public sealed record MigrationManifest(
    DateTimeOffset GeneratedAt,
    string SourceArchive,
    string? PortainerSchemaVersion,
    IReadOnlyList<EndpointManifest> Endpoints,
    IReadOnlyList<ExportedProject> Projects);

public sealed class MigrationSession
{
    public string? BackupPath { get; set; }
    public string? PortainerUrl { get; set; }
    public string ApiKeyEnvironmentVariable { get; set; } = "PORTAINER_API_KEY";
    public string? OutputDirectory { get; set; }
    public HashSet<int> SelectedStackIds { get; set; } = [];
    public HashSet<string> CompletedSteps { get; set; } = [];
}
