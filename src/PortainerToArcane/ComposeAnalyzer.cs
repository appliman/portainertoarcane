using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace PortainerToArcane;

public sealed partial class ComposeAnalyzer
{
    public StackAssessment Analyze(StackInfo stack)
    {
        var findings = new List<PreflightFinding>();
        if (string.IsNullOrWhiteSpace(stack.ComposeContent))
        {
            findings.Add(new("compose.missing", "Fichier Compose absent du backup.", MigrationRisk.High));
            return new(stack, MigrationRisk.High, findings);
        }

        try
        {
            var yaml = new YamlStream();
            yaml.Load(new StringReader(stack.ComposeContent));
        }
        catch (YamlDotNet.Core.YamlException exception)
        {
            findings.Add(new("compose.invalid", $"YAML invalide : {exception.Message}", MigrationRisk.High));
        }

        AddIfMatch(findings, stack.ComposeContent, DockerSocketRegex(), "docker.socket", "Accès au socket Docker.", MigrationRisk.High);
        AddIfMatch(findings, stack.ComposeContent, HostNetworkRegex(), "network.host", "Utilise le réseau host.", MigrationRisk.High);
        AddIfMatch(findings, stack.ComposeContent, PrivilegedRegex(), "container.privileged", "Utilise le mode privilégié.", MigrationRisk.High);
        AddIfMatch(findings, stack.ComposeContent, DeviceRegex(), "container.devices", "Mappe des périphériques hôte.", MigrationRisk.High);
        AddIfMatch(findings, stack.ComposeContent, BindMountRegex(), "storage.bind", "Contient des bind mounts à vérifier sur l'hôte cible.", MigrationRisk.Moderate);
        AddIfMatch(findings, stack.ComposeContent, ExternalResourceRegex(), "resource.external", "Dépend d'un réseau ou volume externe.", MigrationRisk.Moderate);

        var images = ImageRegex().Matches(stack.ComposeContent)
            .Select(static match => match.Groups[1].Value.Trim('\'', '"'))
            .ToArray();
        if (images.Any(IsMutableImage))
        {
            findings.Add(new("image.mutable", "Utilise au moins une image latest ou non épinglée.", MigrationRisk.Moderate));
        }
        if (stack.EnvironmentVariableNames.Any(static name => SensitiveVariableRegex().IsMatch(name)))
        {
            findings.Add(new("environment.sensitive", "Contient des variables potentiellement sensibles.", MigrationRisk.Moderate));
        }

        var risk = findings.Any(static finding => finding.Severity == MigrationRisk.High)
            ? MigrationRisk.High
            : findings.Any(static finding => finding.Severity == MigrationRisk.Moderate)
                ? MigrationRisk.Moderate
                : MigrationRisk.Easy;
        return new(stack, risk, findings);
    }

    private static bool IsMutableImage(string image)
    {
        if (image.Contains("@sha256:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var lastSlash = image.LastIndexOf('/');
        var lastColon = image.LastIndexOf(':');
        return lastColon <= lastSlash || image.EndsWith(":latest", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddIfMatch(
        ICollection<PreflightFinding> findings,
        string content,
        Regex regex,
        string code,
        string message,
        MigrationRisk risk)
    {
        if (regex.IsMatch(content))
        {
            findings.Add(new(code, message, risk));
        }
    }

    [GeneratedRegex(@"docker\.sock", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DockerSocketRegex();

    [GeneratedRegex(@"^\s*network_mode\s*:\s*[""']?host", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex HostNetworkRegex();

    [GeneratedRegex(@"^\s*privileged\s*:\s*true", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex PrivilegedRegex();

    [GeneratedRegex(@"^\s*devices\s*:", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex DeviceRegex();

    [GeneratedRegex(@"^\s*-\s*(?:/[^:\r\n]+|\./[^:\r\n]+):", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex BindMountRegex();

    [GeneratedRegex(@"^\s*external\s*:\s*true", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ExternalResourceRegex();

    [GeneratedRegex(@"^\s*image\s*:\s*([^\s#]+)", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"key|token|secret|password|pwd|credential|connection|string|private|cert", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveVariableRegex();
}
