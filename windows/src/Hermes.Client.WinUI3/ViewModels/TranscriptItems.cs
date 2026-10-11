using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Core;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Hermes.Client.WinUI3.ViewModels;

public abstract partial class TranscriptItem : ObservableObject
{
}

public sealed partial class UserMessageItem : TranscriptItem
{
    public required string Text { get; init; }
}

public sealed partial class AssistantMessageItem : TranscriptItem
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReasoning))]
    public partial string Reasoning { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string Status { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCommentary))]
    public partial string Commentary { get; set; } = "";

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    public string MessageId { get; set; } = "";

    public bool HasReasoning => !string.IsNullOrWhiteSpace(Reasoning);
    public bool HasStatus => !string.IsNullOrWhiteSpace(Status);
    public bool HasCommentary => !string.IsNullOrWhiteSpace(Commentary);
}

public sealed partial class ToolMessageItem : TranscriptItem
{
    public string? ToolName { get; init; }
    public required string Text { get; init; }
}

/// <summary>
/// An outbound message held in the durable queue because the gateway was
/// unreachable. Shows the message text plus its queue state; a failed send
/// surfaces its error with a retry button.
/// </summary>
public sealed partial class QueuedMessageItem : TranscriptItem
{
    private readonly Func<QueuedMessage, Task> _retry;

    public QueuedMessageItem(QueuedMessage message, Func<QueuedMessage, Task> retry)
    {
        Message = message;
        _retry = retry;
    }

    public QueuedMessage Message { get; }

    public string Text => Message.Text;

    [ObservableProperty]
    public partial string Status { get; set; } = "Queued — will send on reconnect.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial bool IsRetrying { get; set; }

    public bool HasError => Error is not null;

    private bool CanRetry() => !IsRetrying && Error is not null;

    [RelayCommand(CanExecute = nameof(CanRetry))]
    public async Task RetryAsync()
    {
        if (IsRetrying)
        {
            return;
        }

        IsRetrying = true;
        Error = null;
        Status = "Sending…";
        try
        {
            await _retry(Message);
        }
        finally
        {
            IsRetrying = false;
        }
    }
}

/// <summary>
/// Dangerous-command approval card. Resolves via POST /v1/runs/{run_id}/approval,
/// keyed by run_id from the SSE envelope. Only the server-advertised choices render.
/// </summary>
public sealed partial class ApprovalItem : TranscriptItem
{
    private readonly IGatewayClient _client;

    public ApprovalItem(IGatewayClient client, ApprovalRequest request)
    {
        _client = client;
        Request = request;
    }

    public ApprovalRequest Request { get; }

    public bool OfferOnce => Request.Choices.Contains("once");
    public bool OfferAlways => Request.Choices.Contains("always");
    public bool OfferDeny => Request.Choices.Contains("deny");

    public string CardName => $"Approval requested: {Request.Command ?? Request.ToolName ?? "unknown command"}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonsVisible))]
    public partial bool IsResolved { get; set; }

    [ObservableProperty]
    public partial string? ResolutionText { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    public bool ButtonsVisible => !IsResolved;

    [RelayCommand]
    public async Task ResolveAsync(string choice)
    {
        if (IsResolved)
        {
            return;
        }

        Error = null;
        try
        {
            await _client.ResolveApprovalAsync(Request.RunId, choice, Request.RequestId);
            IsResolved = true;
            ResolutionText = choice switch
            {
                "once" => "Allowed once.",
                "always" => "Always allowed.",
                "session" => "Allowed for this session.",
                "deny" => "Denied.",
                _ => $"Resolved: {choice}.",
            };
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }
}

public sealed partial class ArtifactItem : ObservableObject
{
    public required string ArtifactId { get; init; }
    public string? FileName { get; init; }
    public string? ContentType { get; init; }
    public string? Sha256 { get; init; }
    public byte[]? Data { get; init; }

    public bool IsImage =>
        ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;

    public bool IsText => !IsImage && (ContentType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true
        || ContentType is "application/json" or "application/xml" or "application/javascript");

    public string DisplayName => FileName ?? ArtifactId;

    public string Meta
    {
        get
        {
            var size = Data?.Length ?? 0;
            var sizeText = size < 1024 ? $"{size} B" : size < 1024 * 1024 ? $"{size / 1024.0:F1} KB" : $"{size / 1048576.0:F1} MB";
            var kind = ContentType ?? "unknown type";
            var sha = Sha256 is { Length: >= 12 } s ? $" • sha256 {s[..12]}…" : "";
            return $"{kind} • {sizeText}{sha}";
        }
    }

    public string? TextPreview =>
        IsText && Data is not null ? Encoding.UTF8.GetString(Data) : null;

    /// <summary>Set on the UI thread after download for image artifacts.</summary>
    public BitmapImage? Image { get; set; }
}
