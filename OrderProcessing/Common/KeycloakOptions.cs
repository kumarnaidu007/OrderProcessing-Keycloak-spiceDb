namespace OrderProcessing.Common;

public sealed class KeycloakOptions
{
    public const string SectionName = "Keycloak";

    /// <summary>Realm base URL for OIDC discovery when not using <see cref="MetadataAuthority"/>.</summary>
    public string Authority { get; set; } = "";

    /// <summary>
    /// Inside Docker, use the service URL (e.g. http://keycloak:8080/realms/name) so JWKS resolves on the compose network.
    /// Tokens may still have iss from browser (localhost); list those under <see cref="ValidIssuers"/>.
    /// </summary>
    public string MetadataAuthority { get; set; } = "";

    /// <summary>Allowed iss claims; required when Issuer differs from metadata host (Docker + localhost tokens).</summary>
    public string[] ValidIssuers { get; set; } = Array.Empty<string>();

    public string Audience { get; set; } = "";
    public bool RequireHttpsMetadata { get; set; } = false;
}
