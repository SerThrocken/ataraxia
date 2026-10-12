using System.Text.Json;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

/// <summary>In-memory <see cref="ISettingsService"/> with the same JSON options as JsonSettingsService.</summary>
internal sealed class FakeSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Dictionary<string, string> _blobs = new();

    public Task<T?> LoadAsync<T>(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(_blobs.TryGetValue(name, out var json)
            ? JsonSerializer.Deserialize<T>(json, Json)
            : default);

    public Task SaveAsync<T>(string name, T value, CancellationToken cancellationToken = default)
    {
        _blobs[name] = JsonSerializer.Serialize(value, Json);
        return Task.CompletedTask;
    }

    public Task<string?> LoadRawAsync(string name, CancellationToken cancellationToken = default) =>
        Task.FromResult(_blobs.TryGetValue(name, out var json) ? json : null);

    public Task SaveRawAsync(string name, string json, CancellationToken cancellationToken = default)
    {
        _blobs[name] = json;
        return Task.CompletedTask;
    }

    public bool HasBlob(string name) => _blobs.ContainsKey(name);

    public string? RawBlob(string name) =>
        _blobs.TryGetValue(name, out var json) ? json : null;
}

/// <summary>In-memory <see cref="ISecretStore"/>.</summary>
internal sealed class FakeSecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _secrets = new();

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_secrets.TryGetValue(key, out var value) ? value : null);

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        _secrets[key] = value;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        _secrets.Remove(key);
        return Task.CompletedTask;
    }

    public bool HasSecret(string key) => _secrets.ContainsKey(key);
}

/// <summary>Recording <see cref="IGatewayEndpoint"/> for switcher tests.</summary>
internal sealed class FakeGatewayEndpoint : IGatewayEndpoint
{
    public readonly List<Uri> BaseUrls = new();
    public readonly List<string?> ApiKeys = new();

    public void SetBaseUrl(Uri baseUrl) => BaseUrls.Add(baseUrl);

    public void SetApiKey(string? apiKey) => ApiKeys.Add(apiKey);
}

/// <summary>Synchronous in-memory <see cref="ITerminalSession"/> for adapter tests.</summary>
internal sealed class FakeTerminalSession : ITerminalSession
{
    public string Title => "fake";

    public bool IsConnected { get; private set; }

    public event EventHandler<TerminalDataEventArgs>? DataReceived;

    public event EventHandler? Disconnected;

    public readonly List<byte[]> Written = new();

    public readonly List<(int Cols, int Rows)> Resizes = new();

    public int CloseCount { get; private set; }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task WriteAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        Written.Add(data);
        return Task.CompletedTask;
    }

    public Task ResizeAsync(int columns, int rows, CancellationToken cancellationToken = default)
    {
        Resizes.Add((columns, rows));
        return Task.CompletedTask;
    }

    public Task CloseAsync()
    {
        CloseCount++;
        IsConnected = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => new(CloseAsync());

    public void Emit(byte[] data) =>
        DataReceived?.Invoke(this, new TerminalDataEventArgs(data));
}

/// <summary>
/// Stand-in for the UI's per-connection live resources (SSE streams, run
/// subscriptions). Records disposal and the order relative to endpoint calls.
/// </summary>
internal sealed class FakeLiveResources : IAsyncDisposable
{
    private readonly Action _onDispose;

    public FakeLiveResources(Action onDispose) => _onDispose = onDispose;

    public bool Disposed { get; private set; }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        _onDispose();
        return ValueTask.CompletedTask;
    }
}
