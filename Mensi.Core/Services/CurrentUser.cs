using Mensi.Core.Options;
using Microsoft.Extensions.Options;

namespace Mensi.Core.Services;

/// <summary>Az audit sorok szerzője. Élesben mindig az Access-claimből jön; fejlesztésben,
/// kikapcsolt Access mellett fix fallback, hogy az audit ott se legyen üres.</summary>
public sealed class CurrentUser(
    IHttpContextAccessor accessor,
    IOptions<CloudflareAccessOptions> access,
    IHostEnvironment environment)
{
    public string Email
    {
        get
        {
            var email = AccessIdentity.Of(accessor.HttpContext);
            if (email != AccessIdentity.Unknown) return email;
            return !access.Value.IsConfigured && environment.IsDevelopment()
                ? AccessIdentity.DevFallback
                : AccessIdentity.Unknown;
        }
    }
}
