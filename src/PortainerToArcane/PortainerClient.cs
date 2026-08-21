using System.Text.Json;

namespace PortainerToArcane;

public sealed class PortainerClient : IDisposable
{
    private readonly HttpClient _httpClient;

    public PortainerClient(string baseUrl, string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/api/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
    }

    public async Task<string?> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync("status", cancellationToken);
        return document.RootElement.TryGetProperty("Version", out var version) ? version.GetString() : null;
    }

    public async Task<IReadOnlyList<EndpointInfo>> GetEndpointsAsync(CancellationToken cancellationToken = default)
    {
        using var document = await GetJsonAsync("endpoints", cancellationToken);
        return document.RootElement.EnumerateArray().Select(item =>
        {
            var snapshot = item.TryGetProperty("Snapshots", out var snapshots) &&
                           snapshots.ValueKind == JsonValueKind.Array &&
                           snapshots.GetArrayLength() > 0
                ? snapshots[0]
                : default;
            return new EndpointInfo(
                item.GetProperty("Id").GetInt32(),
                item.GetProperty("Name").GetString() ?? "endpoint",
                item.GetProperty("Type").GetInt32(),
                item.TryGetProperty("ContainerEngine", out var engine) ? engine.GetString() ?? "docker" : "docker",
                item.GetProperty("Status").GetInt32(),
                GetString(snapshot, "DockerVersion"),
                GetInt(snapshot, "ContainerCount"),
                GetInt(snapshot, "RunningContainerCount"),
                GetInt(snapshot, "StackCount"));
        }).OrderBy(static endpoint => endpoint.Id).ToArray();
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static int GetInt(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.TryGetInt32(out var result) ? result : 0;

    private static string? GetString(JsonElement element, string propertyName) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public void Dispose() => _httpClient.Dispose();
}
