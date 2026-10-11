using Xunit;
using System.Net;
using System.Text;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// HTTP-level tests for the ops surface (<c>GatewayClient.Ops.cs</c>):
/// <c>GET /health/detailed</c> parsing and multi-page session iteration.
/// A stub <see cref="HttpMessageHandler"/> keeps everything offline.
/// </summary>
public sealed class GatewayClientOpsTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }

    private static GatewayClient ClientFor(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)),
            new GatewayOptions { BaseUrl = new Uri("http://127.0.0.1:8642") });

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    [Fact]
    public async Task GetDetailedHealthAsyncParsesLiveShape()
    {
        const string body = """
            {"status": "ok", "platform": "hermes-agent", "version": "0.21.6",
             "gateway_state": "running", "platforms": {"a2a": {"state": "connected"}}}
            """;
        var requested = new List<string>();
        var client = ClientFor(req =>
        {
            requested.Add(req.RequestUri!.PathAndQuery);
            return Json(body);
        });

        var health = await client.GetDetailedHealthAsync();

        Assert.Equal("0.21.6", health.Version);
        Assert.Equal("running", health.GatewayState);
        Assert.Equal("connected", health.Platforms["a2a"].State);
        Assert.Single(requested, "/health/detailed");
    }

    [Fact]
    public async Task GetDetailedHealthAsyncThrowsOnGatewayError()
    {
        var client = ClientFor(_ => Json("""{"error": {"message": "boom"}}""", HttpStatusCode.InternalServerError));

        var ex = await Assert.ThrowsAsync<GatewayApiException>(() => client.GetDetailedHealthAsync());
        Assert.Equal(500, ex.StatusCode);
    }

    private static string PageJson(int limit, int offset, bool hasMore, params string[] ids)
    {
        var items = string.Join(",",
            ids.Select(id =>
                "{\"id\": \"" + id + "\", \"title\": \"t\", \"message_count\": 1, \"pinned\": false, \"archived\": false}"));
        return "{\"object\": \"list\", \"data\": [" + items + "], \"limit\": " + limit
            + ", \"offset\": " + offset + ", \"has_more\": " + (hasMore ? "true" : "false") + "}";
    }

    [Fact]
    public async Task GetAllSessionsAsyncFollowsHasMorePages()
    {
        var offsets = new List<int>();
        var client = ClientFor(req =>
        {
            // Minimal query parse: only "offset" is needed here.
            var query = req.RequestUri!.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            var offset = query.Select(p => p.Split('='))
                .First(p => p[0] == "offset")[1];
            offsets.Add(int.Parse(offset));
            return offsets.Count switch
            {
                1 => Json(PageJson(2, 0, true, "s1", "s2")),
                _ => Json(PageJson(2, 2, false, "s3")),
            };
        });

        var all = await client.GetAllSessionsAsync();

        Assert.Equal(new[] { "s1", "s2", "s3" }, all.Select(s => s.Id).ToArray());
        Assert.Equal(new[] { 0, 100 }, offsets.ToArray()); // impl pages with limit=100
    }

    [Fact]
    public async Task GetAllSessionsAsyncStopsOnSinglePage()
    {
        var calls = 0;
        var client = ClientFor(_ =>
        {
            calls++;
            return Json("""{"object": "list", "data": [], "limit": 50, "offset": 0, "has_more": false}""");
        });

        var all = await client.GetAllSessionsAsync();

        Assert.Empty(all);
        Assert.Equal(1, calls);
    }
}
