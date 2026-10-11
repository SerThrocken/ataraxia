using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Core;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// Chat transcript for one session: history via GET /api/sessions/{id}/messages,
/// turns via POST /api/sessions/{id}/chat/stream (typed SSE). Deltas append per
/// message_id; tool.progress with tool_name "_thinking" renders as reasoning;
/// approval.request surfaces an approval card; artifact download links in turn
/// output are fetched into the artifact pane.
/// </summary>
public sealed partial class ChatViewModel : ObservableObject
{
    private readonly IGatewayClient _client;
    private readonly Dictionary<string, AssistantMessageItem> _liveMessages = new();
    private readonly HashSet<string> _seenArtifacts = new();
    private CancellationTokenSource? _streamCts;
    private string? _sessionId;

    // The gateway emits no artifact events; artifact references surface as
    // download URLs in turn text (verified against gateway source, 2026-10-11).
    private static readonly Regex ArtifactLink =
        new(@"/v1/artifacts/download/([A-Za-z0-9_-]+)", RegexOptions.Compiled);

    public ChatViewModel(IGatewayClient client)
    {
        _client = client;
    }

    public ObservableCollection<TranscriptItem> Transcript { get; } = new();
    public ObservableCollection<ArtifactItem> Artifacts { get; } = new();

    [ObservableProperty]
    public partial string? SessionTitle { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string InputText { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool IsStreaming { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ArtifactPaneVisible))]
    public partial bool HasArtifacts { get; set; }

    public bool HasError => Error is not null;

    public bool ArtifactPaneVisible => HasArtifacts;
    public bool HasSession => _sessionId is not null;

    private bool CanSend() =>
        !IsStreaming && !string.IsNullOrWhiteSpace(InputText) && _sessionId is not null;

    public async Task OpenSessionAsync(string sessionId, string? title)
    {
        CancelStreaming();
        _sessionId = sessionId;
        SessionTitle = string.IsNullOrWhiteSpace(title) ? sessionId : title;
        Transcript.Clear();
        Artifacts.Clear();
        HasArtifacts = false;
        Error = null;
        _liveMessages.Clear();
        _seenArtifacts.Clear();
        SendCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasSession));

        try
        {
            var messages = await _client.GetMessagesAsync(sessionId);
            foreach (var message in messages)
            {
                if (message.DisplayKind == "hidden")
                {
                    continue;
                }

                Transcript.Add(message.Role switch
                {
                    "user" => new UserMessageItem { Text = message.Content },
                    "assistant" => new AssistantMessageItem
                    {
                        Text = message.Content,
                        Reasoning = message.ReasoningContent ?? message.Reasoning ?? "",
                    },
                    "tool" => new ToolMessageItem
                    {
                        ToolName = message.ToolName,
                        Text = message.Content,
                    },
                    _ => new UserMessageItem { Text = message.Content },
                });
            }
        }
        catch (Exception ex)
        {
            Error = FriendlyError(ex);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    public async Task SendAsync()
    {
        var text = InputText.Trim();
        var sessionId = _sessionId;
        if (string.IsNullOrEmpty(text) || sessionId is null || IsStreaming)
        {
            return;
        }

        InputText = "";
        Error = null;
        Transcript.Add(new UserMessageItem { Text = text });
        var assistant = new AssistantMessageItem();
        Transcript.Add(assistant);
        IsStreaming = true;
        _streamCts = new CancellationTokenSource();

        try
        {
            await foreach (var e in _client.StreamChatEventsAsync(sessionId, text, _streamCts.Token))
            {
                using (e)
                {
                    HandleEvent(e, assistant);
                }
            }
        }
        catch (OperationCanceledException)
        {
            assistant.Status = "Cancelled.";
        }
        catch (Exception ex)
        {
            Error = FriendlyError(ex);
            assistant.Status = "Failed.";
        }
        finally
        {
            IsStreaming = false;
            _streamCts?.Dispose();
            _streamCts = null;
        }
    }

    public void CancelStreaming() => _streamCts?.Cancel();

    private AssistantMessageItem ItemFor(string? messageId, AssistantMessageItem current) =>
        messageId is not null && _liveMessages.TryGetValue(messageId, out var item) ? item : current;

    private void HandleEvent(SseEvent e, AssistantMessageItem current)
    {
        switch (e.Name)
        {
            case "run.started":
                current.Status = "Working…";
                break;

            case "message.started":
            {
                var id = e.GetString("message_id");
                if (id is not null)
                {
                    _liveMessages[id] = current;
                }

                break;
            }

            case "assistant.delta":
            {
                var item = ItemFor(e.GetString("message_id"), current);
                if (e.GetString("delta") is { } delta)
                {
                    item.Text += delta;
                }

                item.Status = "";
                break;
            }

            case "tool.progress":
            {
                var item = ItemFor(e.GetString("message_id"), current);
                var delta = e.GetString("delta") ?? "";
                if (e.GetString("tool_name") == "_thinking")
                {
                    item.Reasoning += delta;
                }
                else if (delta.Length > 0)
                {
                    item.Status = delta;
                }

                break;
            }

            case "tool.started":
                current.Status = $"Running {e.GetString("tool_name") ?? "tool"}…";
                break;

            case "tool.completed":
                current.Status = $"{e.GetString("tool_name") ?? "Tool"} finished.";
                ScanArtifacts(e.GetString("preview") ?? "");
                break;

            case "tool.failed":
                current.Status = $"{e.GetString("tool_name") ?? "Tool"} failed.";
                break;

            case "assistant.commentary":
                if (e.GetString("text") is { } commentary)
                {
                    current.Commentary += commentary;
                }

                break;

            case "approval.request":
                current.Status = "";
                Transcript.Add(new ApprovalItem(_client, ApprovalRequest.FromEvent(e)));
                break;

            case "assistant.completed":
            {
                var item = ItemFor(e.GetString("message_id"), current);
                if (e.GetString("content") is { } content)
                {
                    item.Text = content;
                }

                item.Status = "";
                ScanArtifacts(item.Text);
                break;
            }

            case "run.completed":
                current.Status = "";
                break;

            case "run.cancelled":
                current.Status = "Cancelled.";
                break;

            case "run.failed":
                current.Status = "Run failed.";
                Error = e.GetString("message") ?? "The run failed.";
                break;

            case "error":
                Error = e.GetString("message") ?? "Stream error.";
                current.Status = "Failed.";
                break;

            case "done":
                current.Status = "";
                break;
        }
    }

    private void ScanArtifacts(string text)
    {
        foreach (Match match in ArtifactLink.Matches(text))
        {
            var id = match.Groups[1].Value;
            if (_seenArtifacts.Add(id))
            {
                _ = DownloadArtifactAsync(id);
            }
        }
    }

    private async Task DownloadArtifactAsync(string artifactId)
    {
        try
        {
            var download = await _client.DownloadArtifactAsync(artifactId);
            var item = new ArtifactItem
            {
                ArtifactId = download.ArtifactId,
                FileName = download.FileName,
                ContentType = download.ContentType,
                Sha256 = download.Sha256,
                Data = download.Data,
            };
            if (item.IsImage && download.Data is not null)
            {
                item.Image = await LoadImageAsync(download.Data);
            }

            Artifacts.Add(item);
            HasArtifacts = true;
        }
        catch (GatewayApiException ex) when (ex.StatusCode is 404 or 410)
        {
            // One-shot download already consumed, or expired: nothing to show.
        }
        catch (Exception ex)
        {
            Error = FriendlyError(ex);
        }
    }

    private static async Task<BitmapImage> LoadImageAsync(byte[] data)
    {
        var bitmap = new BitmapImage();
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(data.AsBuffer());
        stream.Seek(0);
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    private static string FriendlyError(Exception ex) => ex switch
    {
        GatewayApiException { StatusCode: 401 } =>
            "Invalid gateway API key. Check the connection settings.",
        GatewayApiException api => $"Gateway error {api.StatusCode}: {api.Message}",
        HttpRequestException => "Cannot reach the gateway. Check the connection settings.",
        _ => ex.Message,
    };
}
