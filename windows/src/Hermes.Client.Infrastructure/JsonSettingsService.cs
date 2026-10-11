using System.Text.Json;
using Hermes.Client.Core;

namespace Hermes.Client.Infrastructure;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Hermes.Client", "settings");

    public async Task<T?> LoadAsync<T>(string name, CancellationToken cancellationToken = default)
    {
        var path = PathFor(name);
        if (!File.Exists(path))
        {
            return default;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Json, cancellationToken);
    }

    public async Task SaveAsync<T>(string name, T value, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        await using var stream = File.Create(PathFor(name));
        await JsonSerializer.SerializeAsync(stream, value, Json, cancellationToken);
    }

    public async Task<string?> LoadRawAsync(string name, CancellationToken cancellationToken = default)
    {
        var path = PathFor(name);
        if (!File.Exists(path))
        {
            return null;
        }

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    public async Task SaveRawAsync(string name, string json, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(PathFor(name), json, cancellationToken);
    }

    private string PathFor(string name) => Path.Combine(_directory, $"{name}.json");
}
