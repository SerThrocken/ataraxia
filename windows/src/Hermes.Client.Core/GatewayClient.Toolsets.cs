namespace Hermes.Client.Core;

public sealed partial class GatewayClient
{
    public override async Task<IReadOnlyList<ToolsetInfo>> GetToolsetsAsync(CancellationToken cancellationToken = default)
    {
        var list = await GetJsonAsync<ToolsetList>("v1/toolsets", cancellationToken).ConfigureAwait(false);
        return list?.Data
            .Select(t => new ToolsetInfo(t.Name, t.Label, t.Description, t.Enabled, t.Configured, t.Tools))
            .ToList() ?? [];
    }
}
