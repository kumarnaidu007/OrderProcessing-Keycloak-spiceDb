namespace OrderProcessing.Common;

/// <summary>JWT claim types used in addition to standard role/sub claims.</summary>
public static class AppClaims
{
    /// <summary>Permission code from <see cref="PermissionCodes"/> (one claim per permission).</summary>
    public const string Permission = "perm";

    /// <summary>Active <see cref="UserSession.SessionId"/> after OTP verify.</summary>
    public const string SessionId = "sid";
}
