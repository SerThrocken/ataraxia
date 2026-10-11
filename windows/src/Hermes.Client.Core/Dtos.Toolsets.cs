namespace Hermes.Client.Core;

// Wire shape verified against GET /v1/toolsets on gateway 0.21.6 (2026-10-11):
// {"object":"list","platform":"api_server","data":[{"name","label","description",
// "enabled","configured","tools":[...]}]}. Toolsets are the server-side tool
// bundles the api_server agent exposes; unlike skills they carry an `enabled`
// flag — but the gateway exposes no toggle route (PATCH /v1/toolsets/{name}
// 404s, verified 2026-10-11), so the client treats them as read-only.

public sealed record ToolsetInfo(
    string Name,
    string? Label,
    string? Description,
    bool Enabled,
    bool Configured,
    IReadOnlyList<string> Tools);

internal sealed record ToolsetList(IReadOnlyList<ToolsetDto> Data);

internal sealed record ToolsetDto(
    string Name,
    string? Label,
    string? Description,
    bool Enabled,
    bool Configured,
    IReadOnlyList<string> Tools);
