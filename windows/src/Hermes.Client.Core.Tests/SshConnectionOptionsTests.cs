using Xunit;
using Hermes.Client.Core;
using Hermes.Client.Infrastructure;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// SSH option building/parsing without live connections: pure validation
/// and string handling, no sockets, no keys.
/// </summary>
public sealed class SshConnectionOptionsTests
{
    [Fact]
    public void FromHost_MapsFields()
    {
        var host = new SshHost
        {
            Id = "abc", Name = "Acer", Host = "100.101.250.118",
            Port = 2222, Username = "tlcro", AuthKind = SshAuthKind.Password,
        };

        var options = SshConnectionOptions.FromHost(host);

        Assert.Equal("100.101.250.118", options.Host);
        Assert.Equal(2222, options.Port);
        Assert.Equal("tlcro", options.Username);
        Assert.Equal(SshAuthKind.Password, options.AuthKind);
    }

    [Fact]
    public void Validate_ReturnsNull_WhenValid()
    {
        var options = new SshConnectionOptions
            { Host = "100.101.250.118", Port = 22, Username = "tlcro" };

        Assert.Null(options.Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a host!")]
    [InlineData("http://example.com")]
    public void Validate_RejectsBadHost(string host)
    {
        var options = new SshConnectionOptions
            { Host = host, Port = 22, Username = "tlcro" };

        Assert.NotNull(options.Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Validate_RejectsBadPort(int port)
    {
        var options = new SshConnectionOptions
            { Host = "example.com", Port = port, Username = "tlcro" };

        Assert.NotNull(options.Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsEmptyUsername(string username)
    {
        var options = new SshConnectionOptions
            { Host = "example.com", Port = 22, Username = username };

        Assert.NotNull(options.Validate());
    }

    [Fact]
    public void Describe_Formats_UserAtHostColonPort()
    {
        var options = new SshConnectionOptions
            { Host = "100.101.250.118", Port = 2222, Username = "tlcro" };

        Assert.Equal("tlcro@100.101.250.118:2222", options.Describe());
    }

    [Fact]
    public void Fingerprint_IsStable_OpenSshFormat()
    {
        // SHA256:base64, no padding — the format both platforms pin.
        var a = SshNetTerminalSession.Fingerprint(new byte[] { 1, 2, 3 });
        var b = SshNetTerminalSession.Fingerprint(new byte[] { 1, 2, 3 });

        Assert.Equal(a, b);
        Assert.StartsWith("SHA256:", a);
        Assert.DoesNotContain("=", a);
    }
}
