using System.Net;
using Xunit;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

public sealed class ToolsetsTests
{
    // Wire shape captured from GET /v1/toolsets on gateway 0.21.6 (2026-10-11).
    private const string WireJson = """
        {"object":"list","platform":"api_server","data":[
          {"name":"web","label":"Web Search & Scraping","description":"web_search, web_extract",
           "enabled":true,"configured":true,"tools":["web_extract","web_search"]},
          {"name":"browser","label":"Browser Automation","description":"navigate, click, type, scroll",
           "enabled":false,"configured":false,"tools":["browser_back"]}
        ]}
        """;

    private sealed class FakeHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public HttpResponseMessage Response { get; set; } =
            new(HttpStatusCode.OK) { Content = new StringContent(WireJson) };

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(Response);
        }
    }

    private static GatewayClient ClientFor(FakeHandler handler) =>
        new(new HttpClient(handler), new GatewayOptions { BaseUrl = new Uri("http://127.0.0.1:8642") });

    [Fact]
    public async Task GetToolsetsAsyncRequestsV1Toolsets()
    {
        var handler = new FakeHandler();
        var client = ClientFor(handler);

        await client.GetToolsetsAsync();

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("/v1/toolsets", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetToolsetsAsyncParsesEnabledAndConfigured()
    {
        var client = ClientFor(new FakeHandler());

        var toolsets = await client.GetToolsetsAsync();

        Assert.Equal(2, toolsets.Count);
        var web = toolsets[0];
        Assert.Equal("web", web.Name);
        Assert.Equal("Web Search & Scraping", web.Label);
        Assert.True(web.Enabled);
        Assert.True(web.Configured);
        Assert.Equal(new[] { "web_extract", "web_search" }, web.Tools);
        Assert.False(toolsets[1].Enabled);
    }

    [Fact]
    public async Task GetToolsetsAsyncThrowsGatewayApiExceptionOnServerError()
    {
        var handler = new FakeHandler
        {
            Response = new(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("""{"error":{"message":"Failed to enumerate toolsets","type":"server_error"}}"""),
            },
        };
        var client = ClientFor(handler);

        var ex = await Assert.ThrowsAsync<GatewayApiException>(() => client.GetToolsetsAsync());
        Assert.Equal(500, ex.StatusCode);
    }
}
