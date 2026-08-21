using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

namespace PortainerToArcane.Tests;

public sealed class BackupReaderTests
{
    [Fact]
    public async Task ReadAsync_ReadsPortainerArchiveWithoutExtractingSecrets()
    {
        var root = Path.Combine(Path.GetTempPath(), "portainer-to-arcane-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var archive = Path.Combine(root, "backup.tar.gz");
        try
        {
            await CreateArchiveAsync(archive);

            var snapshot = await new BackupReader().ReadAsync(archive);

            Assert.Single(snapshot.Endpoints);
            var stack = Assert.Single(snapshot.Stacks);
            Assert.Equal("demo", stack.Name);
            Assert.Contains("nginx:1.29.1", stack.ComposeContent, StringComparison.Ordinal);
            Assert.Equal("TOKEN=secret\n", stack.EnvironmentContent);
            Assert.Contains("compose/42/stack.env", snapshot.SensitiveEntries);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task CreateArchiveAsync(string archive)
    {
        await using var file = File.Create(archive);
        await using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        await using var writer = new TarWriter(gzip, leaveOpen: false);
        await WriteEntryAsync(writer, "/compose/42/docker-compose.yml", "services:\n  web:\n    image: nginx:1.29.1\n");
        await WriteEntryAsync(writer, "/compose/42/stack.env", "TOKEN=secret\n");
        await WriteEntryAsync(writer, "/export-1.json", """
            {
              "version": { "SchemaVersion": "1" },
              "users": [{}],
              "registries": [],
              "endpoints": [
                {
                  "Id": 6,
                  "Name": "prod",
                  "Type": 2,
                  "ContainerEngine": "docker",
                  "Status": 1,
                  "Snapshots": [
                    {
                      "DockerVersion": "29.2.1",
                      "ContainerCount": 1,
                      "RunningContainerCount": 1,
                      "StackCount": 1
                    }
                  ]
                }
              ],
              "stacks": [
                {
                  "Id": 42,
                  "Name": "demo",
                  "Type": 2,
                  "EndpointId": 6,
                  "Status": 1,
                  "EntryPoint": "docker-compose.yml",
                  "Env": [{ "name": "TOKEN", "value": "secret" }],
                  "GitConfig": null,
                  "FromAppTemplate": false
                }
              ]
            }
            """);
    }

    private static async Task WriteEntryAsync(TarWriter writer, string name, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
        {
            DataStream = new MemoryStream(bytes, writable: false)
        };
        await writer.WriteEntryAsync(entry);
    }
}
