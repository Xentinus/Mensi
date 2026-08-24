using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Mensi.Core.Options;

namespace Mensi.Core.Services;

/// <summary>
/// Caches the Cloudflare Access signing keys. Cloudflare rotates them, so the set is refreshed
/// on a timer and — when a token arrives with an unknown key id — on demand, throttled so an
/// invalid token cannot turn into a request flood against the certs endpoint.
/// </summary>
public sealed class CloudflareAccessKeyStore(
    IHttpClientFactory httpClientFactory,
    IOptions<CloudflareAccessOptions> options,
    TimeProvider timeProvider,
    ILogger<CloudflareAccessKeyStore> logger)
{
    public const string HttpClientName = "cloudflare-access-certs";

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyCollection<SecurityKey> _keys = [];
    private DateTimeOffset _fetchedAt = DateTimeOffset.MinValue;

    /// <param name="force">
    /// Set after a signature key lookup failure. Still respects <see cref="MinRefreshInterval"/>.
    /// </param>
    public async Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(
        bool force, CancellationToken cancellationToken)
    {
        var minAge = force ? MinRefreshInterval : RefreshInterval;
        if (_keys.Count > 0 && timeProvider.GetUtcNow() - _fetchedAt < minAge) return _keys;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Another request may have refreshed while this one waited for the gate.
            if (_keys.Count > 0 && timeProvider.GetUtcNow() - _fetchedAt < minAge) return _keys;

            var client = httpClientFactory.CreateClient(HttpClientName);
            var json = await client.GetStringAsync(options.Value.CertsUrl, cancellationToken);
            var keys = JsonWebKeySet.Create(json).GetSigningKeys();

            if (keys.Count == 0)
            {
                // Keep whatever still works rather than locking the admin out on a bad response.
                logger.LogWarning("Cloudflare Access certs endpoint returned no signing keys");
                return _keys;
            }

            _keys = [.. keys];
            _fetchedAt = timeProvider.GetUtcNow();
            logger.LogInformation("Loaded {KeyCount} Cloudflare Access signing key(s)", keys.Count);
            return _keys;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A transient certs-endpoint failure must not invalidate the cached keys: with them
            // the host keeps serving, without them every request would be rejected.
            logger.LogError(ex, "Failed to load the Cloudflare Access signing keys");
            _fetchedAt = timeProvider.GetUtcNow();
            return _keys;
        }
        finally
        {
            _gate.Release();
        }
    }
}
