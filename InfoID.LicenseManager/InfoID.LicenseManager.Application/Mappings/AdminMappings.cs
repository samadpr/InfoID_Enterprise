using InfoID.LicenseManager.Application.DTOs.Audit;
using InfoID.LicenseManager.Application.DTOs.Auth;
using InfoID.LicenseManager.Domain.Entities;

namespace InfoID.LicenseManager.Application.Mappings;

internal static class AdminMappings
{
    public static AuthenticatedAdminDto ToAuthenticatedDto(this AdminUser admin) =>
        new(admin.Id, admin.FullName, admin.Email, admin.Role);

    public static AuditLogEntryDto ToDto(this AdminAuditLog entry) =>
        new(entry.Id, entry.ActionDate, entry.AdminUser?.FullName, entry.Action, entry.IPAddress, entry.DetailsJson);
}
