using System.Security.Claims;

namespace OrderProcessing.Services;

public interface ICurrentUserContext
{
    string GetSubject(ClaimsPrincipal user);
}

public sealed class CurrentUserContext : ICurrentUserContext
{
    public string GetSubject(ClaimsPrincipal user)
    {
        return user.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? user.FindFirstValue("sub")
               ?? throw new InvalidOperationException("Authenticated user does not contain sub/nameidentifier.");
    }
}
