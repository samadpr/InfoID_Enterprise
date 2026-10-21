using System.Security.Claims;
using InfoID.LicenseManager.Application.Common;

namespace InfoID.LicenseManager.Web.Security;

/// <summary>Reads the signed-in staff member from the request's claims principal.</summary>
public sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextCurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public long? AdminUserId =>
        long.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);

    public string? Role => Principal?.FindFirstValue(ClaimTypes.Role);

    public string? ClientAddress => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
