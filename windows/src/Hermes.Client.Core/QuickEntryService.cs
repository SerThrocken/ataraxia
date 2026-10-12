namespace Hermes.Client.Core;

/// <summary>
/// Submits quick-entry text (from the global-hotkey box): creates a fresh
/// session via <c>POST /api/sessions</c>, then posts the text as one chat
/// turn via <c>POST /api/sessions/{id}/chat</c>. UI-agnostic — the WinUI3
/// window handles focus/navigation around this call.
/// </summary>
public sealed class QuickEntryService(IGatewayClient client)
{
    public async Task<Session> SubmitAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var session = await client.CreateSessionAsync(cancellationToken: cancellationToken);
        await client.SendChatAsync(session.Id, text, cancellationToken);
        return session;
    }
}
