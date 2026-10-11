using System.Text.Json;
using Hermes.Client.Core;

namespace Hermes.Client.Infrastructure;

/// <summary>
/// JSONL-backed <see cref="IOutboundMessageQueue"/>: one JSON object per
/// line in <c>%LOCALAPPDATA%\Hermes.Client\queue\outbound-queue.jsonl</c>.
/// Every mutation rewrites the whole file atomically (temp file + move)
/// under a semaphore, so a crash can never leave a half-written queue.
/// Corrupt lines are skipped, never fatal. No new dependencies — stdlib only.
/// </summary>
public sealed class FileOutboundMessageQueue : IOutboundMessageQueue
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string FileName = "outbound-queue.jsonl";

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <param name="directory">
    /// Queue directory override. Null (default) = the app's LocalAppData
    /// queue folder. Tests and verification harnesses pass a temp folder.
    /// </param>
    public FileOutboundMessageQueue(string? directory = null)
    {
        var dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Hermes.Client", "queue");
        _path = Path.Combine(dir, FileName);
    }

    public Task<QueuedMessage> EnqueueAsync(string sessionId, string text, CancellationToken cancellationToken = default) =>
        EnqueueAsync(sessionId, text, image: null, cancellationToken);

    public async Task<QueuedMessage> EnqueueAsync(
        string sessionId, string text, ChatImageAttachment? image, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (string.IsNullOrWhiteSpace(text) && image is null)
        {
            throw new ArgumentException("Message text must not be empty.", nameof(text));
        }
        var message = new QueuedMessage(
            Id: Guid.NewGuid().ToString("N"),
            SessionId: sessionId,
            Text: text,
            EnqueuedUtc: DateTime.UtcNow,
            AttemptCount: 0,
            LastError: null,
            ImageData: image?.Data,
            ImageMimeType: image?.MimeType);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var items = ReadAllLocked();
            items.Add(message);
            WriteAllLocked(items);
        }
        finally
        {
            _gate.Release();
        }

        return message;
    }

    public async Task<IReadOnlyList<QueuedMessage>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return ReadAllLocked().OrderBy(m => m.EnqueuedUtc).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkSentAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WriteAllLocked(ReadAllLocked().Where(m => m.Id != id).ToList());
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkFailedAsync(string id, string error, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            WriteAllLocked(ReadAllLocked()
                .Select(m => m.Id == id ? m with { AttemptCount = m.AttemptCount + 1, LastError = error } : m)
                .ToList());
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        (await GetPendingAsync(cancellationToken).ConfigureAwait(false)).Count;

    private List<QueuedMessage> ReadAllLocked()
    {
        var items = new List<QueuedMessage>();
        if (!File.Exists(_path))
        {
            return items;
        }

        foreach (var line in File.ReadLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var message = JsonSerializer.Deserialize<QueuedMessage>(line, Json);
                if (message is not null && !string.IsNullOrEmpty(message.Id))
                {
                    items.Add(message);
                }
            }
            catch (JsonException)
            {
                // Corrupt line: skip it, never fail the read.
            }
        }

        return items;
    }

    private void WriteAllLocked(List<QueuedMessage> items)
    {
        var dir = Path.GetDirectoryName(_path);
        if (dir is not null)
        {
            Directory.CreateDirectory(dir);
        }

        var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var writer = new StreamWriter(temp))
            {
                foreach (var message in items)
                {
                    writer.WriteLine(JsonSerializer.Serialize(message, Json));
                }
            }

            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }
}
