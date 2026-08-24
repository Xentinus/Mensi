using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mensi.Core.Middleware;
using Mensi.Core.Options;
using Mensi.Core.Services;

namespace Mensi.Tests;

/// <summary>
/// The admin surface is protected by nothing else once a request reaches the origin, so every
/// rejection path is asserted here: a forgotten <c>ValidateAudience</c> or a lifetime check that
/// stops firing would otherwise go unnoticed until someone found the open origin.
/// </summary>
public class CloudflareAccessMiddlewareTests : IDisposable
{
    private const string TeamDomain = "https://team.cloudflareaccess.test";
    private const string Audience = "aud-tag-1234";

    private readonly TestSigningKey _key = new("kid-1");

    public void Dispose() => _key.Dispose();

    /// <summary>
    /// Mirrors the admin host's pipeline: everything is gated except the liveness probe, which the
    /// container healthcheck has to reach from inside without an assertion.
    /// </summary>
    private static Task<IHost> ServerAsync(CertsEndpoint certs, TimeProvider? clock = null) =>
        new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.None));
                    services.Configure<CloudflareAccessOptions>(options =>
                    {
                        options.TeamDomain = TeamDomain;
                        options.Audience = Audience;
                    });
                    services.AddSingleton<IHttpClientFactory>(certs);
                    services.AddSingleton(clock ?? TimeProvider.System);
                    services.AddSingleton<CloudflareAccessKeyStore>();
                })
                .Configure(app =>
                {
                    app.UseWhen(
                        context => !context.Request.Path.StartsWithSegments("/health"),
                        gated => gated.UseMiddleware<CloudflareAccessMiddleware>());
                    // /whoami echoes what the request carries as its identity, which is what the
                    // audit log writes into its Actor column.
                    app.Run(context => context.Response.WriteAsync(
                        context.Request.Path == "/whoami"
                            ? AccessIdentity.Of(context)
                            : "reached"));
                }))
            .StartAsync();

    private static async Task<HttpResponseMessage> GetAsync(
        IHost server, string path = "/api/works", string? token = null, string? cookie = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null) request.Headers.Add("Cf-Access-Jwt-Assertion", token);
        if (cookie is not null) request.Headers.Add("Cookie", $"CF_Authorization={cookie}");
        return await server.GetTestClient().SendAsync(request);
    }

    [Fact]
    public async Task A_request_without_an_assertion_is_rejected()
    {
        using var certs = new CertsEndpoint(_key);
        using var server = await ServerAsync(certs);

        var response = await GetAsync(server);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        // The gate answers itself, so the endpoint behind it is never reached.
        Assert.Equal("Forbidden", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_valid_assertion_header_passes_through()
    {
        using var certs = new CertsEndpoint(_key);
        using var server = await ServerAsync(certs);

        var response = await GetAsync(server, token: _key.CreateToken(TeamDomain, Audience));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("reached", await response.Content.ReadAsStringAsync());
    }

    /// <summary>Browser navigations carry the Access session in a cookie, not in the header.</summary>
    [Fact]
    public async Task A_valid_assertion_cookie_passes_through()
    {
        using var certs = new CertsEndpoint(_key);
        using var server = await ServerAsync(certs);

        var response = await GetAsync(server, cookie: _key.CreateToken(TeamDomain, Audience));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_expired_assertion_is_rejected()
    {
        using var certs = new CertsEndpoint(_key);
        using var server = await ServerAsync(certs);

        // Issued an hour ago with a five minute lifetime — well past the one minute clock skew.
        var token = _key.CreateToken(TeamDomain, Audience,
            lifetime: TimeSpan.FromMinutes(5), issuedAgo: TimeSpan.FromHours(1));

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(server, token: token)).StatusCode);
    }

    /// <summary>
    /// A token for another Access application of the same account is signed by the same keys, so
    /// the audience check is the only thing keeping it out.
    /// </summary>
    [Fact]
    public async Task An_assertion_for_another_audience_is_rejected()
    {
        using var certs = new CertsEndpoint(_key);
        using var server = await ServerAsync(certs);

        var token = _key.CreateToken(TeamDomain, "aud-tag-of-another-app");

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(server, token: token)).StatusCode);
    }

    [Fact]
    public async Task An_assertion_from_another_issuer_is_rejected()
    {
        using var certs = new CertsEndpoint(_key);
        using var server = await ServerAsync(certs);

        var token = _key.CreateToken("https://someone-else.cloudflareaccess.test", Audience);

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(server, token: token)).StatusCode);
    }

    /// <summary>The key id is claimed by the token, so a matching one proves nothing on its own.</summary>
    [Fact]
    public async Task An_assertion_signed_by_a_foreign_key_is_rejected()
    {
        using var certs = new CertsEndpoint(_key);
        using var server = await ServerAsync(certs);
        using var attacker = new TestSigningKey("attacker");

        var token = attacker.CreateToken(TeamDomain, Audience, keyIdOverride: _key.KeyId);

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(server, token: token)).StatusCode);
    }

    [Fact]
    public async Task Garbage_in_place_of_an_assertion_is_rejected()
    {
        using var certs = new CertsEndpoint(_key);
        using var server = await ServerAsync(certs);

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(server, token: "not-a-jwt")).StatusCode);
    }

    /// <summary>
    /// An unknown key id triggers one refresh, but the throttle keeps a stream of bogus tokens from
    /// turning into a request flood against the certs endpoint.
    /// </summary>
    [Fact]
    public async Task An_unknown_key_id_is_rejected_without_refetching_the_keys_repeatedly()
    {
        using var certs = new CertsEndpoint(_key);
        var clock = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        using var server = await ServerAsync(certs, clock);
        using var attacker = new TestSigningKey("kid-unknown");

        for (var i = 0; i < 5; i++)
        {
            var token = attacker.CreateToken(TeamDomain, Audience);
            Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(server, token: token)).StatusCode);
        }

        Assert.Equal(1, certs.Calls);
    }

    /// <summary>
    /// Cloudflare rotates the signing keys, so a token signed by a key the cache has not seen must
    /// be given a second chance after one refresh — otherwise every rotation locks the admin out.
    /// </summary>
    [Fact]
    public async Task An_assertion_signed_after_a_key_rotation_is_accepted_after_one_refresh()
    {
        using var certs = new CertsEndpoint(_key);
        var clock = new MutableTimeProvider(DateTimeOffset.UnixEpoch);
        using var server = await ServerAsync(certs, clock);
        using var rotated = new TestSigningKey("kid-2");

        // Warm the cache with the old key, then rotate.
        Assert.Equal(HttpStatusCode.OK,
            (await GetAsync(server, token: _key.CreateToken(TeamDomain, Audience))).StatusCode);
        certs.Publish(rotated);
        // The on-demand refresh is throttled, so the retry only reaches Cloudflare once the
        // minimum interval has passed.
        clock.Advance(TimeSpan.FromMinutes(6));

        var response = await GetAsync(server, token: rotated.CreateToken(TeamDomain, Audience));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, certs.Calls);
    }

    /// <summary>
    /// The container healthcheck calls this from inside, without an assertion. If the gate covered
    /// it, every deploy would end in a restart loop.
    /// </summary>
    [Fact]
    public async Task The_liveness_probe_is_reachable_without_an_assertion()
    {
        using var certs = new CertsEndpoint(_key);
        using var server = await ServerAsync(certs);

        var response = await GetAsync(server, "/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, certs.Calls);
    }

    /// <summary>
    /// With the certs endpoint down and nothing cached there is no way to verify anything, so the
    /// gate has to stay closed rather than fall open.
    /// </summary>
    [Fact]
    public async Task Requests_are_rejected_while_the_signing_keys_cannot_be_loaded()
    {
        using var certs = new CertsEndpoint(_key) { Fails = true };
        using var server = await ServerAsync(certs);

        var token = _key.CreateToken(TeamDomain, Audience);

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(server, token: token)).StatusCode);
    }

    /// <summary>
    /// The assertion carries the signed-in address, and the middleware used to throw it away — so
    /// nothing downstream could say who wrote what. The audit log reads it off the principal.
    /// </summary>
    [Fact]
    public async Task A_valid_assertion_puts_the_signed_in_address_on_the_request()
    {
        using var certs = new CertsEndpoint(_key);
        using var server = await ServerAsync(certs);
        var token = _key.CreateToken(TeamDomain, Audience, email: "owner@example.test");

        var response = await GetAsync(server, "/whoami", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("owner@example.test", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// A token without an address is still a valid assertion, so the request goes through — the log
    /// just cannot name the actor, and says so instead of writing an empty string.
    /// </summary>
    [Fact]
    public async Task An_assertion_without_an_address_leaves_the_actor_unknown()
    {
        using var certs = new CertsEndpoint(_key);
        using var server = await ServerAsync(certs);
        var token = _key.CreateToken(TeamDomain, Audience, email: null);

        var response = await GetAsync(server, "/whoami", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AccessIdentity.Unknown, await response.Content.ReadAsStringAsync());
    }
}
