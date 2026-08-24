using System.Security.Claims;

namespace Mensi.Core.Services;

/// <summary>
/// A kérés mögötti személy. A CloudflareAccessMiddleware validálja az assertiont és a
/// principal-ra teszi az identitást — az audit log innen olvassa vissza az emailt.
/// </summary>
public static class AccessIdentity
{
    /// <summary>Ebbe a claimbe teszi a Cloudflare Access a bejelentkezett email-címet.</summary>
    public const string EmailClaim = "email";

    /// <summary>
    /// Fejlesztésben, kikapcsolt Access mellett minden írás ezen a néven auditálódik.
    /// </summary>
    public const string DevFallback = "dev@localhost";

    public const string Unknown = "unknown";

    public static string Of(HttpContext? context)
    {
        var user = context?.User;
        if (user?.Identity?.IsAuthenticated != true) return Unknown;

        var email = user.FindFirst(EmailClaim)?.Value ?? user.FindFirst(ClaimTypes.Email)?.Value;
        return string.IsNullOrWhiteSpace(email) ? Unknown : email;
    }
}
