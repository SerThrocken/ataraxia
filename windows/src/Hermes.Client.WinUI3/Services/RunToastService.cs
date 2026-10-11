using Hermes.Client.Core;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Hermes.Client.WinUI3.Services;

/// <summary>
/// Windows toast notifications for agent runs. Approval toasts carry
/// Approve/Deny buttons wired to the runs approval endpoint; every toast
/// deep-links back to the run in Mission Control. Activation arguments
/// carry only run ids and request ids — never secrets or key material.
/// </summary>
public sealed class RunToastService
{
    /// <summary>Registers the toast activator. Call once at app launch.</summary>
    public void EnsureRegistered() => AppNotificationManager.Default.Register();

    /// <summary>
    /// Urgent toast for a pending dangerous-command approval. The buttons
    /// resolve via <c>POST /v1/runs/{id}/approval</c> (choices
    /// <c>once</c>/<c>deny</c>, keyed by run id + request id).
    /// </summary>
    public void ShowApproval(string runId, ApprovalRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var builder = new AppNotificationBuilder()
            .SetScenario(AppNotificationScenario.Urgent)
            .AddText("Approval needed")
            .AddText(request.Command ?? request.Description ?? $"Run {ShortId(runId)}")
            .AddButton(new AppNotificationButton("Approve")
                .AddArgument("action", "approve")
                .AddArgument("runId", runId)
                .AddArgument("requestId", request.RequestId ?? string.Empty))
            .AddButton(new AppNotificationButton("Deny")
                .AddArgument("action", "deny")
                .AddArgument("runId", runId)
                .AddArgument("requestId", request.RequestId ?? string.Empty))
            .AddArgument("action", "view")
            .AddArgument("runId", runId);
        AppNotificationManager.Default.Show(builder.BuildNotification());
    }

    /// <summary>Terminal-state toast; tapping it opens the run in Mission Control.</summary>
    public void ShowCompleted(RunStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status.RunId);
        var headline = status.Status switch
        {
            "completed" => "Run completed",
            "failed" => "Run failed",
            "cancelled" => "Run cancelled",
            _ => $"Run {status.Status}",
        };
        var detail = status.Output switch
        {
            { Length: > 0 } o when o.Length > 200 => o[..200] + "…",
            { Length: > 0 } o => o,
            _ => $"Run {ShortId(status.RunId)}",
        };
        var builder = new AppNotificationBuilder()
            .AddText(headline)
            .AddText(detail)
            .AddArgument("action", "view")
            .AddArgument("runId", status.RunId);
        AppNotificationManager.Default.Show(builder.BuildNotification());
    }

    private static string ShortId(string runId) =>
        runId.Length > 12 ? runId[..12] + "…" : runId;
}
