using System.Text.Json;

namespace PortainerToArcane;

public sealed class SessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<MigrationSession> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return new MigrationSession();
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<MigrationSession>(stream, JsonOptions, cancellationToken)
            ?? new MigrationSession();
    }

    public async Task SaveAsync(string path, MigrationSession session, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, session, JsonOptions, cancellationToken);
        }
        File.Move(temporaryPath, fullPath, overwrite: true);
    }
}
