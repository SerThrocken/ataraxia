using Xunit;

namespace Ataraxia.PowerToysRun.Tests;

public sealed class QueryRouterTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_query_routes_home(string? search)
    {
        Assert.IsType<HomeIntent>(QueryRouter.Route(search));
    }

    [Fact]
    public void Bang_prefix_routes_quick_send()
    {
        var intent = Assert.IsType<QuickSendIntent>(QueryRouter.Route("!summarize this"));
        Assert.Equal("summarize this", intent.Message);
    }

    [Fact]
    public void Bang_prefix_trims_message()
    {
        var intent = Assert.IsType<QuickSendIntent>(QueryRouter.Route("!  hello  "));
        Assert.Equal("hello", intent.Message);
    }

    [Fact]
    public void Bare_bang_routes_home()
    {
        Assert.IsType<HomeIntent>(QueryRouter.Route("!"));
    }

    [Theory]
    [InlineData("ask what is the time")]
    [InlineData("ASK what is the time")]
    [InlineData("Ask what is the time")]
    public void Ask_prefix_routes_quick_send(string search)
    {
        var intent = Assert.IsType<QuickSendIntent>(QueryRouter.Route(search));
        Assert.Equal("what is the time", intent.Message);
    }

    [Fact]
    public void Bare_ask_routes_home()
    {
        Assert.IsType<HomeIntent>(QueryRouter.Route("ask"));
    }

    [Theory]
    [InlineData("open")]
    [InlineData("OPEN")]
    [InlineData("Open")]
    public void Open_routes_open_app(string search)
    {
        Assert.IsType<OpenAppIntent>(QueryRouter.Route(search));
    }

    [Fact]
    public void Plain_text_routes_session_search()
    {
        var intent = Assert.IsType<SessionSearchIntent>(QueryRouter.Route("deploy notes"));
        Assert.Equal("deploy notes", intent.Text);
    }

    [Fact]
    public void Search_text_is_trimmed()
    {
        var intent = Assert.IsType<SessionSearchIntent>(QueryRouter.Route("  deploy notes  "));
        Assert.Equal("deploy notes", intent.Text);
    }
}
