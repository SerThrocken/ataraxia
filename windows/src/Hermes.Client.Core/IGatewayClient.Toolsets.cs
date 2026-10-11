namespace Hermes.Client.Core;

public partial interface IGatewayClient
{
    // GET /v1/toolsets
    Task<IReadOnlyList<ToolsetInfo>> GetToolsetsAsync(CancellationToken cancellationToken = default);
}
