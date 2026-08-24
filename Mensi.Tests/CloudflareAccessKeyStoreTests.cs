using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Mensi.Core.Options;
using Mensi.Core.Services;

namespace Mensi.Tests;

/// <summary>
/// The key cache decides how often the certs endpoint is called and what happens when it answers
/// badly. Both directions matter: refreshing too eagerly turns invalid tokens into an outbound
/// request flood, and dropping the cached keys on a transient failure locks the admin out.
/// </summary>
public class CloudflareAccessKeyStoreTests
{
    private const string TeamDomain = "https://team.cloudflareaccess.test";

    private static CloudflareAccessKeyStore Store(CertsEndpoint certs, MutableTimeProvider clock) =>
        new(certs,
            Options.Create(new CloudflareAccessOptions { TeamDomain = TeamDomain, Audience = "aud" }),
            clock,
            NullLogger<CloudflareAccessKeyStore>.Instance);

    private static MutableTimeProvider Clock() => new(DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task The_keys_are_loaded_once_and_then_served_from_the_cache()
    {
        using var key = new TestSigningKey("kid-1");
        using var certs = new CertsEndpoint(key);
        var store = Store(certs, Clock());

        Assert.Single(await store.GetSigningKeysAsync(force: false, default));
        Assert.Single(await store.GetSigningKeysAsync(force: false, default));

        Assert.Equal(1, certs.Calls);
    }

    /// <summary>An hour is the scheduled refresh window; before it nothing is refetched.</summary>
    [Fact]
    public async Task The_cache_is_refreshed_on_the_hour_and_not_before()
    {
        using var key = new TestSigningKey("kid-1");
        using var certs = new CertsEndpoint(key);
        var clock = Clock();
        var store = Store(certs, clock);

        await store.GetSigningKeysAsync(force: false, default);
        clock.Advance(TimeSpan.FromMinutes(59));
        await store.GetSigningKeysAsync(force: false, default);
        Assert.Equal(1, certs.Calls);

        clock.Advance(TimeSpan.FromMinutes(2));
        await store.GetSigningKeysAsync(force: false, default);
        Assert.Equal(2, certs.Calls);
    }

    /// <summary>
    /// A forced refresh follows an unknown key id, which is caller-controlled — so it is throttled
    /// to five minutes rather than served on demand.
    /// </summary>
    [Fact]
    public async Task A_forced_refresh_is_throttled_to_the_minimum_interval()
    {
        using var key = new TestSigningKey("kid-1");
        using var certs = new CertsEndpoint(key);
        var clock = Clock();
        var store = Store(certs, clock);

        await store.GetSigningKeysAsync(force: false, default);
        for (var i = 0; i < 10; i++) await store.GetSigningKeysAsync(force: true, default);
        Assert.Equal(1, certs.Calls);

        clock.Advance(TimeSpan.FromMinutes(5));
        await store.GetSigningKeysAsync(force: true, default);
        Assert.Equal(2, certs.Calls);
    }

    [Fact]
    public async Task A_forced_refresh_picks_up_a_rotated_key()
    {
        using var key = new TestSigningKey("kid-1");
        using var rotated = new TestSigningKey("kid-2");
        using var certs = new CertsEndpoint(key);
        var clock = Clock();
        var store = Store(certs, clock);

        await store.GetSigningKeysAsync(force: false, default);
        certs.Publish(rotated);
        clock.Advance(TimeSpan.FromMinutes(6));

        var keys = await store.GetSigningKeysAsync(force: true, default);

        Assert.Equal("kid-2", Assert.Single(keys).KeyId);
    }

    /// <summary>
    /// A certs endpoint outage must not invalidate what is already cached: with the keys the host
    /// keeps serving, without them every single request would be rejected.
    /// </summary>
    [Fact]
    public async Task A_failing_certs_endpoint_leaves_the_cached_keys_in_place()
    {
        using var key = new TestSigningKey("kid-1");
        using var certs = new CertsEndpoint(key);
        var clock = Clock();
        var store = Store(certs, clock);

        await store.GetSigningKeysAsync(force: false, default);
        certs.Fails = true;
        clock.Advance(TimeSpan.FromHours(2));

        var keys = await store.GetSigningKeysAsync(force: false, default);

        Assert.Equal("kid-1", Assert.Single(keys).KeyId);
    }

    /// <summary>An answer with no usable keys is treated the same way as a failure.</summary>
    [Fact]
    public async Task An_empty_key_set_leaves_the_cached_keys_in_place()
    {
        using var key = new TestSigningKey("kid-1");
        using var certs = new CertsEndpoint(key);
        var clock = Clock();
        var store = Store(certs, clock);

        await store.GetSigningKeysAsync(force: false, default);
        certs.PublishNothing();
        clock.Advance(TimeSpan.FromHours(2));

        var keys = await store.GetSigningKeysAsync(force: false, default);

        Assert.Equal("kid-1", Assert.Single(keys).KeyId);
    }

    /// <summary>
    /// With nothing cached there is nothing to protect, so a failed first load is retried on the
    /// next request instead of waiting out the refresh window.
    /// </summary>
    [Fact]
    public async Task A_failed_first_load_is_retried_and_then_succeeds()
    {
        using var key = new TestSigningKey("kid-1");
        using var certs = new CertsEndpoint(key) { Fails = true };
        var store = Store(certs, Clock());

        Assert.Empty(await store.GetSigningKeysAsync(force: false, default));

        certs.Fails = false;

        Assert.Single(await store.GetSigningKeysAsync(force: false, default));
        Assert.Equal(2, certs.Calls);
    }
}
