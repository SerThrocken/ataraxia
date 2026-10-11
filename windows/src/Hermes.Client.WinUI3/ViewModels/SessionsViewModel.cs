using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hermes.Client.Core;

namespace Hermes.Client.WinUI3.ViewModels;

/// <summary>
/// Paginated session list: GET /api/sessions with limit/offset.
/// Tap (select) a session to open it in chat; refresh re-loads page one.
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

    private static string FriendlyError(Exception ex) => ex switch
    {
        GatewayApiException { StatusCode: 401 } =>
            "Invalid gateway API key. Check the connection settings.",
        GatewayApiException api => $"Gateway error {api.StatusCode}: {api.Message}",
        HttpRequestException => "Cannot reach the gateway. Check the connection settings.",
        _ => ex.Message,
    };
}
