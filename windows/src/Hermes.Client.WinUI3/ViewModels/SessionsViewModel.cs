using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Core;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// One node of the session fork tree, built from
/// <see cref="Session.ParentSessionId"/> references.
/// </summary>
public sealed partial class ForkNode : ObservableObject
{
    public ForkNode(Session session) => Session = session;

    public Session Session { get; }

    public ObservableCollection<ForkNode> Children { get; } = new();

    public string DisplayTitle =>
        string.IsNullOrWhiteSpace(Session.Title) ? Session.Id : Session.Title;
}

/// <summary>
/// Paginated session list: GET /api/sessions with limit/offset.
/// Tap (select) a session to open it in chat; refresh re-loads page one.
/// The fork tree (<see cref="ForkRoots"/>) is rebuilt from the loaded
/// sessions' <c>ParentSessionId</c> references after every load.
/// </summary>
public sealed partial class SessionsViewModel : ObservableObject
{
    private readonly IGatewayClient _client;
    private const int PageSize = 50;
    private int _offset;

    public SessionsViewModel(IGatewayClient client)
    {
        _client = client;
    }

    public ObservableCollection<Session> Sessions { get; } = new();

    /// <summary>Forest of fork trees: sessions without a known parent are roots.</summary>
    public ObservableCollection<ForkNode> ForkRoots { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadMoreVisible))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadMoreVisible))]
    public partial bool HasMore { get; set; }

    public bool HasError => Error is not null;

    public bool LoadMoreVisible => HasMore && !IsLoading;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        _offset = 0;
        await LoadPageAsync(reset: true);
    }

    [RelayCommand]
    public async Task LoadMoreAsync() => await LoadPageAsync(reset: false);

    private async Task LoadPageAsync(bool reset)
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        Error = null;
        try
        {
            var page = await _client.GetSessionsPageAsync(PageSize, reset ? 0 : _offset);
            if (reset)
            {
                Sessions.Clear();
                _offset = 0;
            }

            foreach (var session in page.Sessions)
            {
                Sessions.Add(session);
            }

            _offset += page.Sessions.Count;
            HasMore = page.HasMore;
            RebuildForkTree();
        }
        catch (Exception ex)
        {
            Error = FriendlyError(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void RebuildForkTree()
    {
        var nodes = Sessions
            .GroupBy(s => s.Id)
            .Select(g => new ForkNode(g.First()))
            .ToDictionary(n => n.Session.Id);
        var roots = new List<ForkNode>();
        foreach (var node in nodes.Values)
        {
            var parentId = node.Session.ParentSessionId;
            if (!string.IsNullOrEmpty(parentId)
                && nodes.TryGetValue(parentId, out var parent)
                && !ReferenceEquals(parent, node))
            {
                parent.Children.Add(node);
            }
            else
            {
                roots.Add(node);
            }
        }

        ForkRoots.Clear();
        foreach (var root in roots.OrderByDescending(r => r.Session.LastActive))
        {
            SortChildren(root);
            ForkRoots.Add(root);
        }
    }

    private static void SortChildren(ForkNode node)
    {
        var ordered = node.Children.OrderByDescending(c => c.Session.LastActive).ToList();
        node.Children.Clear();
        foreach (var child in ordered)
        {
            SortChildren(child);
            node.Children.Add(child);
        }
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
