using System.Diagnostics;
using System.IO;
using Wox.Plugin;

namespace Ataraxia.PowerToysRun;

/// <summary>
/// PowerToys Run plugin for Ataraxia (keyword <c>ax</c>).
/// <list type="bullet">
/// <item><c>ax</c> — recent sessions + "Open Ataraxia".</item>
/// <item><c>ax &lt;text&gt;</c> — sessions matching the text, then
/// "Ask Ataraxia" to send the text as a first message.</item>
/// <item><c>ax !message</c> / <c>ax ask message</c> — send the message to a
/// brand-new session.</item>
/// <item><c>ax open</c> — launch the Ataraxia app.</item>
/// </list>
/// Session results deep-link into the app via the <c>ataraxia://</c> protocol
/// (<c>ataraxia://session/{id}</c>), which the WinUI3 app registers.
/// </summary>
public sealed class Main : IPlugin, IContextMenu, IDisposable
{
    public string Name => "Ataraxia";

    public string Description => "Ataraxia gateway: sessions, quick chat, deep links.";

    private GatewayConnector? _connector;
    private string _iconPath = "";
    private bool _hasIcon;
    private bool _disposed;

    public void Init(PluginInitContext context)
    {
        var directory = context.CurrentPluginMetadata.PluginDirectory;
        _connector = new GatewayConnector(directory);
        // Icons are optional (not committed to the repo): when present,
        // Images/icon.dark.png is used for result rows.
        _iconPath = Path.Combine(directory, "Images", "icon.dark.png");
        _hasIcon = File.Exists(_iconPath);
    }

    public List<Result> Query(Query query)
    {
        try
        {
            return QueryRouter.Route(query.Search) switch
            {
                HomeIntent => HomeResults(),
                SessionSearchIntent s => SearchResults(s.Text),
                QuickSendIntent q => [ToResult(ResultMapper.MapQuickSend(q.Message))],
                OpenAppIntent => [ToResult(ResultMapper.MapOpenApp())],
                _ => [],
            };
        }
        catch (Exception ex)
        {
            return [ErrorResult(ex)];
        }
    }

    public List<ContextMenuResult> LoadContextMenus(Result selectedResult)
    {
        if (selectedResult.ContextData is not string sessionId
            || string.IsNullOrWhiteSpace(sessionId))
        {
            return [];
        }

        return
        [
            new ContextMenuResult
            {
                PluginName = Name,
                Title = "Open in Ataraxia",
                Action = _ =>
                {
                    OpenSession(sessionId);
                    return true;
                },
            },
        ];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connector?.Dispose();
    }

    private List<Result> HomeResults()
    {
        var items = ResultMapper.MapSessions(Connector().GetSessions(), filter: null)
            .Select(ToResult)
            .ToList();
        items.Add(ToResult(ResultMapper.MapOpenApp()));
        return items;
    }

    private List<Result> SearchResults(string text)
    {
        var items = ResultMapper.MapSessions(Connector().GetSessions(), text)
            .Select(ToResult)
            .ToList();
        items.Add(ToResult(ResultMapper.MapQuickSend(text)));
        return items;
    }

    private Result ToResult(PluginResultItem item)
    {
        var result = new Result
        {
            Title = item.Title,
            SubTitle = item.SubTitle,
            QueryTextDisplay = item.Title,
            ContextData = item switch
            {
                OpenSessionItem s => s.SessionId,
                _ => null,
            },
            Action = _ =>
            {
                Invoke(item);
                return true;
            },
        };
        if (_hasIcon)
        {
            result.IcoPath = _iconPath;
        }

        return result;
    }

    private void Invoke(PluginResultItem item)
    {
        switch (item)
        {
            case OpenSessionItem s:
                OpenSession(s.SessionId);
                break;
            case QuickSendItem q:
                QuickSend(q.Message);
                break;
            case OpenAppItem:
                OpenDeepLink("ataraxia://");
                break;
        }
    }

    private void QuickSend(string message)
    {
        var session = Connector().CreateSession(
            message.Length <= 40 ? message : message[..40]);
        Connector().SendChat(session.Id, message);
        OpenSession(session.Id);
    }

    private static void OpenSession(string sessionId) =>
        OpenDeepLink($"ataraxia://session/{Uri.EscapeDataString(sessionId)}");

    private static void OpenDeepLink(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch
        {
            // Protocol not registered (app never launched): nothing to open.
            // The launcher still hides; the error result path explains setup.
        }
    }

    private Result ErrorResult(Exception ex)
    {
        var result = new Result
        {
            Title = "Ataraxia: gateway unreachable",
            SubTitle = TrimMessage(ex),
            Action = _ =>
            {
                OpenDeepLink("ataraxia://");
                return true;
            },
        };
        if (_hasIcon)
        {
            result.IcoPath = _iconPath;
        }

        return result;
    }

    private static string TrimMessage(Exception ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        message = message.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return message.Length <= 120 ? message : message[..120] + "…";
    }

    private GatewayConnector Connector() =>
        _connector ?? throw new InvalidOperationException("Plugin not initialized.");
}
