using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Mensi.Tests;

/// <summary>
/// An RSA signing key standing in for one of Cloudflare's, with the JWKS entry that publishes it
/// and a token factory. The real certs endpoint hands out exactly this shape.
/// </summary>
internal sealed class TestSigningKey(string keyId) : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);

    public string KeyId { get; } = keyId;

    /// <summary>The public half, as the certs endpoint would publish it.</summary>
    public string JwkJson()
    {
        var parameters = _rsa.ExportParameters(includePrivateParameters: false);
        var modulus = Base64UrlEncoder.Encode(parameters.Modulus!);
        var exponent = Base64UrlEncoder.Encode(parameters.Exponent!);
        return $$"""{"kty":"RSA","use":"sig","alg":"RS256","kid":"{{KeyId}}","n":"{{modulus}}","e":"{{exponent}}"}""";
    }

    /// <param name="email">
    /// The claim Cloudflare Access puts the signed-in address in, and the one the audit log reads
    /// back as its actor. <c>null</c> stands for a token that carries no address at all.
    /// </param>
    public string CreateToken(
        string issuer,
        string audience,
        TimeSpan? lifetime = null,
        TimeSpan? issuedAgo = null,
        string? keyIdOverride = null,
        string? email = "admin@example.test")
    {
        var issuedAt = DateTime.UtcNow - (issuedAgo ?? TimeSpan.Zero);
        var claims = new Dictionary<string, object> { ["sub"] = "admin@example.test" };
        if (email is not null) claims["email"] = email;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = issuedAt + (lifetime ?? TimeSpan.FromMinutes(30)),
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new RsaSecurityKey(_rsa) { KeyId = keyIdOverride ?? KeyId },
                SecurityAlgorithms.RsaSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public void Dispose() => _rsa.Dispose();
}

/// <summary>
/// Stands in for <c>/cdn-cgi/access/certs</c>: serves the key set it is given, counts the calls
/// so the refresh throttling is observable, and can be told to fail or to rotate.
/// </summary>
internal sealed class CertsEndpoint(params TestSigningKey[] keys) : HttpMessageHandler, IHttpClientFactory
{
    private string _body = KeySet(keys);

    public int Calls { get; private set; }

    /// <summary>When set, the endpoint fails instead of answering — a transient outage.</summary>
    public bool Fails { get; set; }

    /// <summary>Replaces the published key set, as a Cloudflare key rotation would.</summary>
    public void Publish(params TestSigningKey[] rotated) => _body = KeySet(rotated);

    /// <summary>Answers with a syntactically valid but empty key set.</summary>
    public void PublishNothing() => _body = """{"keys":[]}""";

    public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        if (Fails) throw new HttpRequestException("certs endpoint unreachable");
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(_body, Encoding.UTF8, "application/json"),
        });
    }

    private static string KeySet(TestSigningKey[] keys) =>
        $"{{\"keys\":[{string.Join(',', keys.Select(k => k.JwkJson()))}]}}";
}

/// <summary>A clock the test moves by hand, to exercise the key cache's refresh windows.</summary>
internal sealed class MutableTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
