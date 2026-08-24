using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Mensi.Core.Options;
using Mensi.Core.Services;

namespace Mensi.Core.Middleware;

/// <summary>
/// Verifies the Cloudflare Access assertion on every request, so the admin surface is protected by
/// the application itself and not only by where the container port happens to be published. Without
/// this, a request sent straight to the origin — bypassing the Access login on the hostname — would
/// reach the write endpoints and the contact inbox unauthenticated.
/// <para>
/// Mensi has a single host, and everything behind it is gated by this middleware. It still lives
/// here next to the other middleware, rather than in the host project, so the test project can
/// exercise it by referencing Core alone.
/// </para>
/// </summary>
public class CloudflareAccessMiddleware(
    RequestDelegate next,
    CloudflareAccessKeyStore keyStore,
    IOptions<CloudflareAccessOptions> options,
    ILogger<CloudflareAccessMiddleware> logger)
{
    /// <summary>Set by the Cloudflare edge on every proxied request.</summary>
    private const string AssertionHeader = "Cf-Access-Jwt-Assertion";

    /// <summary>Fallback for browser navigations that carry the Access session as a cookie.</summary>
    private const string AssertionCookie = "CF_Authorization";

    private static readonly JsonWebTokenHandler TokenHandler = new();

    public async Task InvokeAsync(HttpContext context)
    {
        var token = context.Request.Headers[AssertionHeader].ToString();
        if (string.IsNullOrEmpty(token)) token = context.Request.Cookies[AssertionCookie] ?? "";

        if (string.IsNullOrEmpty(token))
        {
            await RejectAsync(context, "missing Cloudflare Access assertion");
            return;
        }

        var result = await ValidateAsync(token, context.RequestAborted);
        if (!result.IsValid)
        {
            await RejectAsync(context, result.Exception?.Message ?? "invalid Cloudflare Access assertion");
            return;
        }

        // The validated identity used to be dropped here, which meant the one thing the assertion
        // carries besides "this request is allowed" — who is behind it — never reached the
        // application. The audit log is what reads it back off the principal; see AccessIdentity.
        if (result.ClaimsIdentity is not null) context.User = new ClaimsPrincipal(result.ClaimsIdentity);

        await next(context);
    }

    private async Task<TokenValidationResult> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        var parameters = await BuildParametersAsync(force: false, cancellationToken);
        var result = await TokenHandler.ValidateTokenAsync(token, parameters);

        // Cloudflare rotates the signing keys, so an unknown key id is expected after a rotation
        // and only means the cached set is stale — refresh once and give the token a second chance.
        if (result.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            parameters = await BuildParametersAsync(force: true, cancellationToken);
            result = await TokenHandler.ValidateTokenAsync(token, parameters);
        }

        return result;
    }

    private async Task<TokenValidationParameters> BuildParametersAsync(
        bool force, CancellationToken cancellationToken)
    {
        var keys = await keyStore.GetSigningKeysAsync(force, cancellationToken);
        return new TokenValidationParameters
        {
            ValidIssuer = options.Value.Issuer,
            ValidAudience = options.Value.Audience,
            IssuerSigningKeys = keys,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    }

    /// <summary>
    /// 403 rather than 401: there is nothing useful to challenge with here, the login happens on the
    /// Cloudflare edge. The reason is logged but never sent back.
    /// </summary>
    private async Task RejectAsync(HttpContext context, string reason)
    {
        logger.LogWarning(
            "Rejected unauthenticated request to {Path} from {RemoteIp}: {Reason}",
            context.Request.Path, context.Connection.RemoteIpAddress, reason);
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync("Forbidden");
    }
}
