namespace Hermes.Client.Core;

/// <summary>
/// The HUD's polling engine: gateway reachability via the real
/// <see cref="IGatewayClient.CheckHealthAsync"/> probe plus per-run status
/// via <c>GET /v1/runs/{id}</c> for every tracked run. Runs that reach a
/// terminal status (or 404) are untracked automatically so polling stops for
/// them. The background loop is fully cancellable: <see cref="StopAsync"/>
/// and <see cref="Dispose"/> leave no polling behind after the overlay
/// closes. UI-agnostic — the view layer marshals <see cref="SnapshotChanged"/>
/// onto its own dispatcher.
/// </summary>
public sealed class HudMonitor : IDisposable
{
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(10);

    private readonly IGatewayClient _client;
    private readonly TimeSpan _pollInterval;
    private readonly HashSet<string> _tracked = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private bool _disposed;

    public HudMonitor(IGatewayClient client, TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _pollInterval = pollInterval ?? DefaultPollInterval;
    }

    /// <summary>Fires on the polling thread, only when the snapshot's content changed.</summary>
    public event EventHandler<HudSnapshot>? SnapshotChanged;

    public HudSnapshot Latest { get; private set; } = HudSnapshot.Unknown;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _loopTask is { IsCompleted: false };
            }
        }
    }

    public IReadOnlyCollection<string> TrackedRunIds
    {
        get
        {
            lock (_gate)
            {
                return _tracked.ToArray();
            }
        }
    }

    /// <summary>Tracks a run id (idempotent); polled on the next pass.</summary>
    public void TrackRun(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            _tracked.Add(runId);
        }
    }

    public bool UntrackRun(string runId)
    {
        lock (_gate)
        {
            return _tracked.Remove(runId);
        }
    }

    public void UntrackAll()
    {
        lock (_gate)
        {
            _tracked.Clear();
        }
    }

    /// <summary>Starts the background poll loop. Idempotent.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            if (_loopTask is { IsCompleted: false })
            {
                return;
            }

            _loopCts?.Dispose();
            _loopCts = new CancellationTokenSource();
            _loopTask = LoopAsync(_loopCts.Token);
        }
    }

    /// <summary>Stops the loop and waits for the in-flight pass to finish.</summary>
    public async Task StopAsync()
    {
        Task? loop;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            loop = _loopTask;
            cts = _loopCts;
            _loopTask = null;
            _loopCts = null;
        }

        cts?.Cancel();
        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on cancellation.
            }
        }

        cts?.Dispose();
    }

    /// <summary>
    /// One full poll pass: gateway health, then each tracked run. Used by the
    /// loop and directly by tests. Never throws for gateway-side failures —
    /// they surface as <see cref="HudConnectionState.Disconnected"/> or a
    /// retained last-known run status.
    /// </summary>
    public async Task RefreshOnceAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        HudConnectionState connection;
        try
        {
            connection = await _client.CheckHealthAsync(cancellationToken).ConfigureAwait(false)
                ? HudConnectionState.Connected
                : HudConnectionState.Disconnected;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            connection = HudConnectionState.Disconnected;
        }

        string[] ids;
        lock (_gate)
        {
            ids = _tracked.ToArray();
        }

        var summaries = new List<HudRunSummary>(ids.Length);
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var status = await _client.GetRunAsync(id, cancellationToken).ConfigureAwait(false);
                summaries.Add(new HudRunSummary(id, status.Status, !status.IsTerminal, status.LastEvent));
                if (status.IsTerminal)
                {
                    UntrackRun(id);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (GatewayApiException ex) when (ex.StatusCode == 404)
            {
                // Run expired gateway-side; stop polling it.
                UntrackRun(id);
            }
            catch (Exception)
            {
                // Keep the run tracked; the next pass retries.
                summaries.Add(new HudRunSummary(id, "unknown", true, null));
            }
        }

        Publish(new HudSnapshot(connection, summaries, DateTimeOffset.UtcNow));
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RefreshOnceAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // A failed pass must not kill the loop; the snapshot already
                // reflects the failure, and the next pass retries.
            }

            try
            {
                await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void Publish(HudSnapshot snapshot)
    {
        if (snapshot.SameContentAs(Latest))
        {
            return;
        }

        Latest = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _loopCts;
            _loopCts = null;
            _loopTask = null;
        }

        cts?.Cancel();
        cts?.Dispose();
    }
}
