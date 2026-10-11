using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Core;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// Full-text search over cached sessions and their messages. The view model
/// fetches (bounded, cancellable) and <see cref="SessionSearch"/> — the pure
/// Core engine — ranks the hits. One bad session's messages never kill the
/// whole search.
/// </summary>
public sealed partial class SearchViewModel : ObservableObject
{
    private const int PageSize = 100;
    private const int SessionCap = 300;

    private readonly IGatewayClient _client;
    private CancellationTokenSource? _cts;

    public SearchViewModel(IGatewayClient client)
    {
        _client = client;
    }

    public ObservableCollection<SearchHit> Hits { get; } = new();

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSearching { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } =
        "Type a query and press Enter to search session titles and message content.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    public bool HasError => Error is not null;

    /// <summary>Raised when the user opens a hit (Enter); the page navigates to chat.</summary>
    public event EventHandler<SearchHit>? OpenHitRequested;

    public void OpenHit(SearchHit hit) => OpenHitRequested?.Invoke(this, hit);

    [RelayCommand]
    public async Task SearchAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var query = Query.Trim();
        Hits.Clear();
        Error = null;
        if (query.Length == 0)
        {
            Status = "Type a query and press Enter to search session titles and message content.";
            return;
        }

        IsSearching = true;
        try
        {
            var sessions = new List<Session>();
            var offset = 0;
            while (sessions.Count < SessionCap)
            {
                Status = $"Loading sessions ({sessions.Count} so far)…";
                var page = await _client.GetSessionsPageAsync(PageSize, offset, ct);
                sessions.AddRange(page.Sessions);
                if (!page.HasMore || page.Sessions.Count == 0)
                {
                    break;
                }

                offset += page.Sessions.Count;
            }

            var messages = new Dictionary<string, IReadOnlyList<ChatMessage>>();
            for (var i = 0; i < sessions.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                Status = $"Searching messages in session {i + 1} of {sessions.Count}…";
                try
                {
                    messages[sessions[i].Id] = await _client.GetMessagesAsync(sessions[i].Id, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // One unreadable session must not kill the whole search.
                }
            }

            var hits = SessionSearch.Search(sessions, messages, query);
            foreach (var hit in hits)
            {
                Hits.Add(hit);
            }

            Status = hits.Count == 0
                ? $"No matches for \"{query}\" in {sessions.Count} sessions."
                : $"{hits.Count} match{(hits.Count == 1 ? "" : "es")} in {sessions.Count} sessions.";
        }
        catch (OperationCanceledException)
        {
            Status = "Search cancelled.";
        }
        catch (Exception ex)
        {
            Error = FriendlyError(ex);
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    public void CancelSearch() => _cts?.Cancel();

    private static string FriendlyError(Exception ex) => ex switch
    {
        GatewayApiException { StatusCode: 401 } =>
            "Invalid gateway API key. Check the connection settings.",
        GatewayApiException api => $"Gateway error {api.StatusCode}: {api.Message}",
        HttpRequestException => "Cannot reach the gateway. Check the connection settings.",
        _ => ex.Message,
    };
}
