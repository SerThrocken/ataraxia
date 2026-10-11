// Display rows for the messaging-platforms page. The gateway (0.21.6)
// exposes platform connections only as a read-only name-keyed table inside
// GET /health/detailed: there is no messaging-platform config API (the only
// platform route is POST /api/platforms/{platform}/events, an inbound event
// callback), so rows carry status text only — never configuration.
namespace Hermes.Client.Core;

/// <summary>One row of the read-only platform status table.</summary>
public sealed record PlatformStatusRow(string Name, string State, string Detail);

/// <summary>
/// Builds sorted display rows from the name-keyed <c>platforms</c> table of
/// GET /health/detailed. State is shown verbatim (no invented mapping);
/// detail joins the listener base and any error text the gateway reported.
/// </summary>
public static class PlatformStatusSummary
{
    public static IReadOnlyList<PlatformStatusRow> Build(DetailedHealth? health)
    {
        if (health?.Platforms is null)
        {
            return [];
        }

        var rows = new List<PlatformStatusRow>(health.Platforms.Count);
        foreach (var (name, status) in health.Platforms
                     .OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            rows.Add(new PlatformStatusRow(name, status.State, BuildDetail(status)));
        }

        return rows;
    }

    private static string BuildDetail(PlatformStatus status)
    {
        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(status.ListenerBase))
        {
            parts.Add(status.ListenerBase);
        }

        var error = status.ErrorMessage;
        if (string.IsNullOrWhiteSpace(error))
        {
            error = status.ErrorCode;
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            parts.Add(error);
        }

        return string.Join(" • ", parts);
    }
}
