namespace PortainerToArcane.Tests;

public sealed class ArcaneExporterTests
{
    [Theory]
    [InlineData("My Stack", "my-stack")]
    [InlineData("../../escape", "..-..-escape")]
    [InlineData("demo/api", "demo-api")]
    public void SanitizeName_ProducesSafeDirectoryName(string value, string expected)
    {
        Assert.Equal(expected, ArcaneExporter.SanitizeName(value));
    }

    [Fact]
    public async Task ExportAsync_WritesComposeEnvironmentAndManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "portainer-to-arcane-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var stack = new StackInfo(
                42,
                "demo",
                2,
                6,
                1,
                "docker-compose.yml",
                ["TOKEN"],
                "services:\n  web:\n    image: nginx:1.29.1\n",
                "TOKEN=secret\n",
                "Editor/Upload");
            var snapshot = new BackupSnapshot(
                "backup.tar.gz",
                "1",
                [new EndpointInfo(6, "prod", 2, "docker", 1)],
                [stack],
                0,
                1,
                0,
                []);
            var assessment = new ComposeAnalyzer().Analyze(stack);

            var manifest = await new ArcaneExporter().ExportAsync(snapshot, [assessment], root);

            Assert.Single(manifest.Projects);
            Assert.True(File.Exists(Path.Combine(root, "endpoint-6-prod", "demo", "compose.yaml")));
            Assert.True(File.Exists(Path.Combine(root, "endpoint-6-prod", "demo", ".env")));
            Assert.True(File.Exists(Path.Combine(root, "migration-manifest.json")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
