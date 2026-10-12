using Hermes.Client.Core;
using Hermes.Client.Infrastructure;

namespace Hermes.Client.Core.Tests;

/// <summary>
/// DPAPI secret-store round-trips. DPAPI exists only on Windows, so these
/// tests return immediately (pass vacuously) on other OSes — the Acer
/// Release build runs them for real.
/// </summary>
public sealed class DpapiSecretStoreTests
{
    private static bool IsWindows => OperatingSystem.IsWindows();

    [Fact]
    public async Task RoundTrip_SetGetDelete()
    {
        if (!IsWindows)
        {
            return;
        }

        var store = new DpapiSecretStore();
        const string key = $"dpapi-test-{Guid.NewGuid():N}";
        const string value = "super-secret-value-1234567890";

        try
        {
            Assert.Null(await store.GetAsync(key));
            await store.SetAsync(key, value);
            Assert.Equal(value, await store.GetAsync(key));
            await store.DeleteAsync(key);
            Assert.Null(await store.GetAsync(key));
        }
        finally
        {
            await store.DeleteAsync(key);
        }
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenMissing()
    {
        if (!IsWindows)
        {
            return;
        }

        var store = new DpapiSecretStore();
        Assert.Null(await store.GetAsync($"dpapi-missing-{Guid.NewGuid():N}"));
    }

    [Fact]
    public async Task SetAsync_OverwritesExisting()
    {
        if (!IsWindows)
        {
            return;
        }

        var store = new DpapiSecretStore();
        var key = $"dpapi-test-{Guid.NewGuid():N}";

        try
        {
            await store.SetAsync(key, "first");
            await store.SetAsync(key, "second");
            Assert.Equal("second", await store.GetAsync(key));
        }
        finally
        {
            await store.DeleteAsync(key);
        }
    }
}
