namespace OrderProcessing.Common;

/// <summary>JWT claim types used in addition to standard role/sub claims.</summary>
public static class AppClaims
{
    /// <summary>Permission code from <see cref="PermissionCodes"/> when present (legacy; Keycloak roles are primary).</summary>
    public const string Permission = "perm";
}
