using Xunit;
using System.Text.Json;
using Hermes.Client.Core;

namespace Hermes.Client.Core.Tests;

public sealed class DtoDeserializationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [Fact]
    public void SessionPageDeserializesContractShape()
    {
        const string json = """
            {
              "object": "list",
              "data": [{
                "id": "20260915_064553_415568",
                "title": "Diagnose restart errors",
                "preview": "/goal look at my computer's error logs...",
                "message_count": 131,
                "model": "openai/gpt-5.6-luna-pro",
                "started_at": 1789472754.969396,
                "ended_at": null,
                "last_active": 1789833984.674899,
                "pinned": false,
                "archived": false
              }],
              "limit": 1, "offset": 0, "has_more": true
            }
            """;

        var page = JsonSerializer.Deserialize<SessionPage>(json, Json)!;

        Assert.True(page.HasMore);
        Assert.Equal(1, page.Limit);
        var s = Assert.Single(page.Sessions);
        Assert.Equal("20260915_064553_415568", s.Id);
        Assert.Equal("Diagnose restart errors", s.Title);
        Assert.Equal(131, s.MessageCount);
        Assert.Equal("openai/gpt-5.6-luna-pro", s.Model);
        Assert.Null(s.EndedAt);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeMilliseconds((long)(1789833984.674899 * 1000)),
            s.LastActive);
    }

    [Fact]
    public void ChatMessageDeserializesStringAndPartsContent()
    {
        const string json = """
            {
              "object": "list", "session_id": "s1",
              "data": [
                {"id":"m1","session_id":"s1","role":"user","content":"hello","timestamp":1791734804.7},
                {"id":"m2","session_id":"s1","role":"assistant",
                 "content":[{"type":"text","text":"hi "},{"type":"text","text":"there"}],
                 "timestamp":1791734811.58,"finish_reason":"stop"},
                {"id":"m3","session_id":"s1","role":"assistant","content":null,"display_kind":"hidden"}
              ],
              "pagination": {"limit":500,"offset":0,"order":"latest","returned":3}
            }
            """;

        var list = JsonSerializer.Deserialize<MessageList>(json, Json)!;

        Assert.Equal(3, list.Messages.Count);
        Assert.Equal("hello", list.Messages[0].Content);
        Assert.Equal("hi there", list.Messages[1].Content);
        Assert.Equal("stop", list.Messages[1].FinishReason);
        Assert.Equal("", list.Messages[2].Content);
        Assert.Equal("hidden", list.Messages[2].DisplayKind);
    }

    [Fact]
    public void ChatMessageAcceptsNumericId()
    {
        const string json = """
            {"id": 42, "session_id": "s1", "role": "user", "content": "hi", "timestamp": 1791734804.7}
            """;

        var message = JsonSerializer.Deserialize<ChatMessage>(json, Json)!;

        Assert.Equal("42", message.Id);
        Assert.Equal("hi", message.Content);
    }

    [Fact]
    public void ApprovalRequestParsesFromSseEvent()
    {
        const string json = """
            {"command":"rm -rf /tmp/x","tool_name":"exec","description":"clean temp",
             "choices":["once","always","deny"],"request_id":"req_1",
             "session_id":"s1","run_id":"r1","seq":9,"ts":1.0}
            """;
        using var doc = JsonDocument.Parse(json);
        using var e = new SseEvent("approval.request", doc, "s1", "r1", 9, 1.0);

        var approval = ApprovalRequest.FromEvent(e);

        Assert.Equal("r1", approval.RunId);
        Assert.Equal("rm -rf /tmp/x", approval.Command);
        Assert.Equal("exec", approval.ToolName);
        Assert.Equal(["once", "always", "deny"], approval.Choices);
        Assert.Equal("req_1", approval.RequestId);
    }

    [Fact]
    public void GatewayApiExceptionCarriesContractErrorShape()
    {
        const string body = """{"error":{"message":"Invalid gateway API key (API_SERVER_KEY)","type":"gateway_auth_error","code":"gateway_auth_failed"}}""";

        var ex = GatewayApiException.FromResponse(401, body);

        Assert.Equal(401, ex.StatusCode);
        Assert.Equal("gateway_auth_error", ex.ErrorType);
        Assert.Equal("gateway_auth_failed", ex.ErrorCode);
        Assert.Contains("Invalid gateway API key", ex.Message);
    }

    [Fact]
    public void GatewayApiExceptionHandlesPlainText404Body()
    {
        var ex = GatewayApiException.FromResponse(404, "404: Not Found");

        Assert.Equal(404, ex.StatusCode);
        Assert.Contains("404: Not Found", ex.Message);
    }
}
