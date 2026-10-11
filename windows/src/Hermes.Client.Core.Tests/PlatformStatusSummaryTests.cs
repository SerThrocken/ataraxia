using Hermes.Client.Core;
using Xunit;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// Tests for <see cref="PlatformStatusSummary.Build"/>: the read-only
/// platform-status table shown by the messaging page, sourced from the
/// name-keyed <c>platforms</c> object of GET /health/detailed.
/// </summary>
public sealed class PlatformStatusSummaryTests
{
    private static DetailedHealth HealthOf(params (string Name, PlatformStatus Status)[] entries) =>
        new("ok", null, null, "0.21.6", "running")
        {
            Platforms = entries.ToDictionary(e => e.Name, e => e.Status),
        };

    private static PlatformStatus Status(
        string state,
        string? listenerBase = null,
        string? errorCode = null,
        string? errorMessage = null) =>
        new(state, errorCode, errorMessage, null, ListenerBase: listenerBase);

    [Fact]
    public void Build_ReturnsEmpty_ForNullHealth()
    {
        Assert.Empty(PlatformStatusSummary.Build(null));
    }

    [Fact]
    public void Build_ReturnsEmpty_WhenPlatformsTableEmpty()
    {
        Assert.Empty(PlatformStatusSummary.Build(HealthOf()));
    }

    [Fact]
    public void Build_SortsRowsByNameOrdinal()
    {
        var rows = PlatformStatusSummary.Build(HealthOf(
            ("zeta", Status("connected")),
            ("api_server", Status("connected")),
            ("a2a", Status("error"))));

        Assert.Equal(
            new[] { "a2a", "api_server", "zeta" },
            rows.Select(r => r.Name).ToArray());
    }

    [Fact]
    public void Build_UsesStateVerbatim()
    {
        var rows = PlatformStatusSummary.Build(HealthOf(
            ("a2a", Status("retrying"))));

        Assert.Equal("retrying", rows[0].State);
    }

    [Fact]
    public void Build_DetailJoinsListenerAndErrorMessage()
    {
        var rows = PlatformStatusSummary.Build(HealthOf(
            ("api_server", Status("error", listenerBase: "0.0.0.0:8642", errorMessage: "bind failed"))));

        Assert.Equal("0.0.0.0:8642 • bind failed", rows[0].Detail);
    }

    [Fact]
    public void Build_PrefersErrorMessageOverErrorCode()
    {
        var rows = PlatformStatusSummary.Build(HealthOf(
            ("a2a", Status("error", errorCode: "E_CONN", errorMessage: "connection refused"))));

        Assert.Equal("connection refused", rows[0].Detail);
    }

    [Fact]
    public void Build_FallsBackToErrorCode_WhenMessageAbsent()
    {
        var rows = PlatformStatusSummary.Build(HealthOf(
            ("a2a", Status("error", errorCode: "E_CONN"))));

        Assert.Equal("E_CONN", rows[0].Detail);
    }

    [Fact]
    public void Build_EmptyDetail_WhenNoListenerOrError()
    {
        var rows = PlatformStatusSummary.Build(HealthOf(
            ("a2a", Status("connected"))));

        Assert.Equal(string.Empty, rows[0].Detail);
    }
}
