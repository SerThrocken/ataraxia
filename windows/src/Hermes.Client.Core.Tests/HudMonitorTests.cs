using Xunit;
using System.Net;
using System.Text;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// <see cref="HudMonitor"/> polling lifecycle: health + run polling against a
/// stub gateway, auto-untrack of terminal/404 runs, change-gated events, and
/// — critically — no HTTP traffic after <see cref="HudMonitor.StopAsync"/> or
/// <see cref="HudMonitor.Dispose"/>. The stub handler counts requests so a
/// leaked background loop fails the test.
/// </summary>
public sealed class HudMonitorTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public int RequestCount;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
            _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref RequestCount);
            return Task.FromResult(_respond(request));
        }
    }

    private static (GatewayClient Client, StubHandler Handler) ClientFor(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var client = new GatewayClient(
            new HttpClient(handler),
            new GatewayOptions { BaseUrl = new Uri("http://127.0.0.1:8642") });
        return (client, handler);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage RunStatusJson(string runId, string status) =>
        Json($$"""{"run_id":"{{runId}}","status":"{{status}}","created_at":1720000000,"updated_at":1720000060}""");

    [Fact]
    public async Task HealthyGatewayPublishesConnectedSnapshot()
    {
        var (client, _) = ClientFor(req =>
            req.RequestUri!.AbsolutePath == "/health"
                ? Json("""{"status":"ok","platform":"test","version":"0.21.6"}""")
                : RunStatusJson("run_1", "running"));

        using var monitor = new HudMonitor(client);
        monitor.TrackRun("run_1");
        await monitor.RefreshOnceAsync();

        Assert.Equal(HudConnectionState.Connected, monitor.Latest.Connection);
        Assert.Equal(HudDotKind.Working, monitor.Latest.Dot);
        Assert.NotNull(monitor.Latest.ActiveRun);
        Assert.Equal("run_1", monitor.Latest.ActiveRun.RunId);
    }

    [Fact]
    public async Task UnreachableGatewayPublishesDisconnectedSnapshot()
    {
        var (client, _) = ClientFor(_ => throw new HttpRequestException("no route"));

        using var monitor = new HudMonitor(client);
        await monitor.RefreshOnceAsync();

        Assert.Equal(HudConnectionState.Disconnected, monitor.Latest.Connection);
        Assert.Equal(HudDotKind.Error, monitor.Latest.Dot);
        Assert.Contains("unreachable", monitor.Latest.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Non200HealthPublishesDisconnectedSnapshot()
    {
        var (client, _) = ClientFor(req =>
            req.RequestUri!.AbsolutePath == "/health"
                ? Json("{}", HttpStatusCode.ServiceUnavailable)
                : RunStatusJson("run_1", "running"));

        using var monitor = new HudMonitor(client);
        await monitor.RefreshOnceAsync();

        Assert.Equal(HudConnectionState.Disconnected, monitor.Latest.Connection);
    }

    [Fact]
    public async Task TerminalRunIsUntrackedAfterOnePass()
    {
        var (client, _) = ClientFor(req =>
            req.RequestUri!.AbsolutePath == "/health"
                ? Json("""{"status":"ok","platform":"test","version":"0.21.6"}""")
                : RunStatusJson("run_done", "completed"));

        using var monitor = new HudMonitor(client);
        monitor.TrackRun("run_done");
        await monitor.RefreshOnceAsync();

        Assert.Empty(monitor.TrackedRunIds);
        Assert.Null(monitor.Latest.ActiveRun);
        Assert.Equal(HudDotKind.Ok, monitor.Latest.Dot);
    }

    [Fact]
    public async Task MissingRunIsUntrackedOn404()
    {
        var (client, _) = ClientFor(req =>
            req.RequestUri!.AbsolutePath == "/health"
                ? Json("""{"status":"ok","platform":"test","version":"0.21.6"}""")
                : Json("""{"detail":"not found"}""", HttpStatusCode.NotFound));

        using var monitor = new HudMonitor(client);
        monitor.TrackRun("run_gone");
        await monitor.RefreshOnceAsync();

        Assert.Empty(monitor.TrackedRunIds);
    }

    [Fact]
    public async Task RunFetchFailureKeepsTheRunTrackedForRetry()
    {
        var (client, _) = ClientFor(req =>
            req.RequestUri!.AbsolutePath == "/health"
                ? Json("""{"status":"ok","platform":"test","version":"0.21.6"}""")
                : throw new HttpRequestException("flaky"));

        using var monitor = new HudMonitor(client);
        monitor.TrackRun("run_flaky");
        await monitor.RefreshOnceAsync();

        Assert.Contains("run_flaky", monitor.TrackedRunIds);
        Assert.Equal("unknown", monitor.Latest.Runs.Single().Status);
    }

    [Fact]
    public void TrackRunIsIdempotentAndRejectsBlanks()
    {
        var (client, _) = ClientFor(_ => Json("{}"));
        using var monitor = new HudMonitor(client);

        monitor.TrackRun("run_1");
        monitor.TrackRun("run_1");

        Assert.Single(monitor.TrackedRunIds);
        Assert.Throws<ArgumentException>(() => monitor.TrackRun("  "));
    }

    [Fact]
    public async Task SnapshotChangedFiresOnlyOnMaterialChange()
    {
        var (client, _) = ClientFor(req =>
            req.RequestUri!.AbsolutePath == "/health"
                ? Json("""{"status":"ok","platform":"test","version":"0.21.6"}""")
                : RunStatusJson("run_1", "running"));

        using var monitor = new HudMonitor(client);
        var events = 0;
        monitor.SnapshotChanged += (_, _) => events++;

        monitor.TrackRun("run_1");
        await monitor.RefreshOnceAsync(); // Unknown -> Connected+running: fires
        await monitor.RefreshOnceAsync(); // identical: silent
        await monitor.RefreshOnceAsync(); // identical: silent

        Assert.Equal(1, events);
    }

    [Fact]
    public async Task StartIsIdempotentAndStopEndsPolling()
    {
        var (client, handler) = ClientFor(_ =>
            Json("""{"status":"ok","platform":"test","version":"0.21.6"}"""));

        using var monitor = new HudMonitor(client, TimeSpan.FromMilliseconds(50));
        monitor.Start();
        monitor.Start(); // second start is a no-op
        Assert.True(monitor.IsRunning);

        await Task.Delay(300); // let a few passes run
        var countAtStop = handler.RequestCount;
        Assert.True(countAtStop > 0);

        await monitor.StopAsync();
        Assert.False(monitor.IsRunning);

        await Task.Delay(200);
        Assert.Equal(countAtStop, handler.RequestCount); // no leaked polling
    }

    [Fact]
    public async Task DisposeCancelsTheLoop()
    {
        var (client, handler) = ClientFor(_ =>
            Json("""{"status":"ok","platform":"test","version":"0.21.6"}"""));

        var monitor = new HudMonitor(client, TimeSpan.FromMilliseconds(50));
        monitor.Start();
        await Task.Delay(200);
        var countAtDispose = handler.RequestCount;
        Assert.True(countAtDispose > 0);

        monitor.Dispose();
        await Task.Delay(200);
        Assert.Equal(countAtDispose, handler.RequestCount);
    }

    [Fact]
    public async Task RefreshAfterDisposeThrows()
    {
        var (client, _) = ClientFor(_ => Json("{}"));
        var monitor = new HudMonitor(client);
        monitor.Dispose();

        Assert.Throws<ObjectDisposedException>(() => monitor.TrackRun("run_1"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => monitor.RefreshOnceAsync());
    }
}
