namespace InfoID.LicenseManager.Application.Interfaces.Repositories;

/// <summary>
/// One unit of work per request: repositories stage changes, services commit them with a
/// single <see cref="SaveChangesAsync"/>. (EF Core's DbContext is the unit of work; this
/// abstraction keeps the application layer free of it.)
/// </summary>
public interface IUnitOfWork
{
    /// <summary>Names a non-staff actor (e.g. "LicenseApi", "CustomerSignUp") for the
    /// CreatedBy/ModifiedBy columns of the changes saved next. Defaults to the signed-in
    /// staff member's e-mail.</summary>
    void UseActor(string actor);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="work"/> in one database transaction: everything it saves commits together
    /// when it returns, and rolls back if it throws. Row locks taken inside (see
    /// <c>ILicenseRepository.FindForUpdateAsync</c>) are held until then.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken = default);
}
