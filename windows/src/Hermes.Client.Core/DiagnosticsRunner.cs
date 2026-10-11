namespace Hermes.Client.Core;

/// <summary>Outcome of one diagnostic check.</summary>
public enum DiagnosticStatus
{
    Pass,
    Warn,
    Fail,
}

public sealed record DiagnosticCheck(string Name, DiagnosticStatus Status, string Detail);

public sealed record DiagnosticsReport(IReadOnlyList<DiagnosticCheck> Checks)
{
    public bool AllPassed => Checks.All(c => c.Status == DiagnosticStatus.Pass);

    public int FailCount => Checks.Count(c => c.Status == DiagnosticStatus.Fail);

    public int WarnCount => Checks.Count(c => c.Status == DiagnosticStatus.Warn);

    /// <summary>Screen-reader summary: "5 passed, 1 warning" etc.</summary>
    public string SummaryText
    {
        get
        {
            var passed = Checks.Count - FailCount - WarnCount;
            var parts = new List<string> { $"{passed} passed" };
            if (WarnCount > 0)
            {
                parts.Add($"{WarnCount} warning{(WarnCount == 1 ? "" : "s")}");
            }

            if (FailCount > 0)
            {
                parts.Add($"{FailCount} failed");
            }

            return string.Join(", ", parts);
        }
    }
}

/// <summary>
/// One-tap diagnostics against the verified 8642 API surface.
/// <c>POST /api/ops/doctor</c> is serve-backend-only on the Acer (registered
/// in <c>hermes_cli/web_routers/ops.py</c>; absent from the 8642 route table
/// and the /v1/capabilities endpoint list, probed 404 on 2026-10-11), so
/// this runner performs the equivalent checks natively: reachability,
/// detailed health (incl. readiness), disk headroom, platform health, model
/// catalog, and session store. Checks never throw; failures are reported.
/// </summary>
public sealed class DiagnosticsRunner(IGatewayClient client)
{
    private const double DiskWarnPercent = 90.0;

    public async Task<DiagnosticsReport> RunAsync(CancellationToken cancellationToken = default)
    {
        DetailedHealth? detailed = null;
        string? detailedError = null;
        try
        {
            detailed = await client.GetDetailedHealthAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            detailedError = Trim(ex.Message);
        }

        var checks = new List<DiagnosticCheck>
        {
            await ReachabilityAsync(cancellationToken).ConfigureAwait(false),
            DetailedHealthCheck(detailed, detailedError),
            DiskCheck(detailed),
            PlatformCheck(detailed),
            await ModelCatalogAsync(cancellationToken).ConfigureAwait(false),
            await SessionStoreAsync(cancellationToken).ConfigureAwait(false),
        };
        return new DiagnosticsReport(checks);
    }

    private async Task<DiagnosticCheck> ReachabilityAsync(CancellationToken ct)
    {
        try
        {
            var ok = await client.CheckHealthAsync(ct).ConfigureAwait(false);
            return ok
                ? new DiagnosticCheck("Gateway reachable", DiagnosticStatus.Pass, "GET /health answered 200.")
                : new DiagnosticCheck("Gateway reachable", DiagnosticStatus.Fail, "GET /health did not answer 200.");
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck("Gateway reachable", DiagnosticStatus.Fail, $"Request failed: {Trim(ex.Message)}");
        }
    }

    private static DiagnosticCheck DetailedHealthCheck(DetailedHealth? detailed, string? error)
    {
        const string name = "Detailed health";
        if (detailed is null)
        {
            return new DiagnosticCheck(name, DiagnosticStatus.Fail, $"GET /health/detailed failed: {error}");
        }

        var bad = detailed.Readiness?.Checks is { } checks
            ? BadCheckNames(checks).ToList()
            : [];
        if (bad.Count > 0)
        {
            return new DiagnosticCheck(name, DiagnosticStatus.Fail,
                $"readiness failing: {string.Join(", ", bad)}");
        }

        return new DiagnosticCheck(name, DiagnosticStatus.Pass,
            $"readiness {detailed.Readiness?.Status ?? "unknown"} · gateway {detailed.Version ?? "?"}");
    }

    private static IEnumerable<string> BadCheckNames(CheckSet checks)
    {
        foreach (var (name, check) in new (string, CheckStatus?)[]
                 {
                     ("state_db", checks.StateDb), ("session_store", checks.SessionStore),
                     ("config", checks.Config), ("model", checks.Model),
                     ("disk", checks.Disk), ("gateway", checks.Gateway),
                     ("background_queues", checks.BackgroundQueues),
                 })
        {
            if (check is not null && !string.Equals(check.Status, "ok", StringComparison.OrdinalIgnoreCase))
            {
                yield return name;
            }
        }
    }

    private static DiagnosticCheck DiskCheck(DetailedHealth? detailed)
    {
        const string name = "Disk space";
        var disk = detailed?.Readiness?.Checks.Disk;
        if (disk is null)
        {
            return new DiagnosticCheck(name, DiagnosticStatus.Warn, "disk check absent from readiness.");
        }

        var detail = $"{disk.UsedPercent:F1}% used ({FormatBytes(disk.FreeBytes)} free)";
        return disk.UsedPercent >= DiskWarnPercent
            ? new DiagnosticCheck(name, DiagnosticStatus.Warn, detail + " — getting full.")
            : new DiagnosticCheck(name, DiagnosticStatus.Pass, detail);
    }

    private static DiagnosticCheck PlatformCheck(DetailedHealth? detailed)
    {
        const string name = "Platforms";
        if (detailed is null)
        {
            return new DiagnosticCheck(name, DiagnosticStatus.Fail, "no detailed health to inspect.");
        }

        var troubled = detailed.Platforms
            .Where(kv => kv.Value.NeedsAttention
                || !string.Equals(kv.Value.State, "connected", StringComparison.OrdinalIgnoreCase))
            .Select(kv => $"{kv.Key}: {kv.Value.State}" +
                (string.IsNullOrWhiteSpace(kv.Value.ErrorMessage) ? "" : $" ({kv.Value.ErrorMessage})"))
            .ToList();

        return troubled.Count == 0
            ? new DiagnosticCheck(name, DiagnosticStatus.Pass,
                $"{detailed.Platforms.Count} platform{(detailed.Platforms.Count == 1 ? "" : "s")} connected.")
            : new DiagnosticCheck(name, DiagnosticStatus.Fail,
                $"{troubled.Count} need attention — {string.Join("; ", troubled)}");
    }

    private async Task<DiagnosticCheck> ModelCatalogAsync(CancellationToken ct)
    {
        const string name = "Model catalog";
        try
        {
            var models = await client.GetModelsAsync(ct).ConfigureAwait(false);
            return models.Count > 0
                ? new DiagnosticCheck(name, DiagnosticStatus.Pass, $"GET /v1/models listed {models.Count} models.")
                : new DiagnosticCheck(name, DiagnosticStatus.Warn, "GET /v1/models returned an empty catalog.");
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck(name, DiagnosticStatus.Fail, $"GET /v1/models failed: {Trim(ex.Message)}");
        }
    }

    private async Task<DiagnosticCheck> SessionStoreAsync(CancellationToken ct)
    {
        const string name = "Session store";
        try
        {
            await client.GetSessionsPageAsync(1, 0, ct).ConfigureAwait(false);
            return new DiagnosticCheck(name, DiagnosticStatus.Pass, "GET /api/sessions reachable.");
        }
        catch (Exception ex)
        {
            return new DiagnosticCheck(name, DiagnosticStatus.Fail, $"GET /api/sessions failed: {Trim(ex.Message)}");
        }
    }

    private static string FormatBytes(long bytes) =>
        bytes switch
        {
            >= 1L << 40 => $"{bytes / (double)(1L << 40):F1} TiB",
            >= 1L << 30 => $"{bytes / (double)(1L << 30):F1} GiB",
            >= 1L << 20 => $"{bytes / (double)(1L << 20):F1} MiB",
            _ => $"{bytes} B",
        };

    private static string Trim(string? value, int maxLength = 200) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Length <= maxLength ? value : value[..maxLength] + "…";
}
