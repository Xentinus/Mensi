namespace Mensi.Core.Options;

/// <summary>
/// Cloudflare Access application the admin host trusts. Both values come from the Zero Trust
/// dashboard: the team domain is the account's Access hostname, the audience is the
/// application's AUD tag.
/// </summary>
public class CloudflareAccessOptions
{
    public const string SectionName = "CloudflareAccess";

    /// <summary>e.g. <c>https://myteam.cloudflareaccess.com</c> — also the expected token issuer.</summary>
    public string TeamDomain { get; set; } = "";

    /// <summary>Application Audience (AUD) tag of the Access application.</summary>
    public string Audience { get; set; } = "";

    public string Issuer => TeamDomain.TrimEnd('/');

    /// <summary>Public signing keys of the account, rotated by Cloudflare.</summary>
    public string CertsUrl => $"{Issuer}/cdn-cgi/access/certs";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TeamDomain) && !string.IsNullOrWhiteSpace(Audience);
}
