using InfoID.Licensing.Server.Entities;
using InfoID.Licensing.Server.Services;
using InfoID.Shared.Contracts.Accounts;

namespace InfoID.LicenseApi;

/// <summary>Customer accounts, plans and license requests (the app's Account &amp;
/// License screens). Sign-in is optional for customers; activation codes never need it.</summary>
internal static class AccountEndpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        var account = app.MapGroup("/api/v1/account").RequireRateLimiting("account");

        account.MapPost("/signup", async (SignUpRequest r, CustomerAccountService accounts) =>
            ToResponse(await accounts.SignUpAsync(new SignUpInput(r.FullName, r.CompanyName, r.Email, r.Phone, r.Country, r.Password, r.AcceptTerms))));

        account.MapPost("/verify", async (VerifyEmailRequest r, CustomerAccountService accounts) =>
            ToResponse(await accounts.VerifyEmailAsync(r.Email, r.Code, r.AppVersion)));

        account.MapPost("/resend", async (EmailOnlyRequest r, CustomerAccountService accounts) =>
            ToResponse(await accounts.ResendVerificationAsync(r.Email)));

        account.MapPost("/login", async (LogInRequest r, CustomerAccountService accounts) =>
            ToResponse(await accounts.LogInAsync(r.Email, r.Password, r.AppVersion)));

        account.MapPost("/forgot", async (EmailOnlyRequest r, CustomerAccountService accounts) =>
            ToResponse(await accounts.ForgotPasswordAsync(r.Email)));

        account.MapPost("/reset", async (ResetPasswordRequest r, CustomerAccountService accounts) =>
            ToResponse(await accounts.ResetPasswordAsync(r.Email, r.Code, r.NewPassword, r.AppVersion)));

        account.MapPost("/logout", async (HttpContext http, CustomerAccountService accounts) =>
        {
            if (BearerToken(http) is { } token) await accounts.LogOutAsync(token);
            return Results.Ok(new AccountResponse { Success = true });
        });

        account.MapGet("/me", async (HttpContext http, CustomerAccountService accounts) =>
            await accounts.GetAccountByTokenAsync(BearerToken(http)) is { } a
                ? Results.Ok(new AccountResponse { Success = true, Account = ToProfile(a) })
                : Results.Unauthorized());

        account.MapGet("/licenses", async (HttpContext http, CustomerAccountService accounts) =>
        {
            if (await accounts.GetAccountByTokenAsync(BearerToken(http)) is not { } a) return Results.Unauthorized();
            var list = await accounts.GetLicensesAsync(a.Id);
            return Results.Ok(list.Select(l => new AccountLicenseDto
            {
                ActivationCode = l.ActivationCode,
                ProductCode = l.ProductCode,
                Tier = l.Tier,
                LicenseType = l.LicenseType,
                ExpiryDate = l.ExpiryDate,
                MaxActivations = l.MaxActivations,
                ActiveMachines = l.ActiveMachines,
                Status = l.Status,
            }).ToList());
        });

        account.MapGet("/requests", async (HttpContext http, CustomerAccountService accounts) =>
        {
            if (await accounts.GetAccountByTokenAsync(BearerToken(http)) is not { } a) return Results.Unauthorized();
            var list = await accounts.GetRequestsAsync(a.Id);
            return Results.Ok(list.Select(r => new AccountRequestDto
            {
                Id = r.Id,
                PlanName = r.PlanName,
                Machines = r.Machines,
                Status = r.Status,
                StatusReason = r.StatusReason,
                CreatedDate = r.CreatedDate,
                ActivationCode = r.ActivationCode,
            }).ToList());
        });

        // Trial printing (signed-in accounts without an activation code). The server owns
        // the count: reserve before printing, then commit (printed) or release.
        var trial = app.MapGroup("/api/v1/trial").RequireRateLimiting("trial");

        trial.MapGet("", async (HttpContext http, CustomerAccountService accounts, TrialService trials) =>
        {
            if (await accounts.GetAccountByTokenAsync(BearerToken(http)) is not { } a) return Results.Unauthorized();
            return Results.Ok(ToDto(await trials.GetStatusAsync(a.Id), new TrialStatusDto { Success = true }));
        });

        trial.MapPost("/reserve", async (TrialReserveRequest r, HttpContext http, CustomerAccountService accounts, TrialService trials) =>
        {
            if (await accounts.GetAccountByTokenAsync(BearerToken(http)) is not { } a) return Results.Unauthorized();
            var result = await trials.ReserveAsync(a.Id, r.Cards, r.MachineFingerprint, r.AppVersion, r.DesignName);
            var response = ToDto(result.Status, new TrialReserveResponse
            {
                Success = result.Succeeded,
                Message = result.Error,
                ReservationId = result.ReservationKey,
                ExpiresUtc = result.ExpiresUtc,
            });
            return Results.Ok(response);
        });

        trial.MapPost("/commit", async (TrialCompleteRequest r, HttpContext http, CustomerAccountService accounts, TrialService trials) =>
        {
            if (await accounts.GetAccountByTokenAsync(BearerToken(http)) is not { } a) return Results.Unauthorized();
            var result = await trials.CommitAsync(a.Id, r.ReservationId, r.CardsPrinted);
            return Results.Ok(ToDto(result.Status, new TrialStatusDto { Success = result.Succeeded, Message = result.Error }));
        });

        trial.MapPost("/release", async (TrialCompleteRequest r, HttpContext http, CustomerAccountService accounts, TrialService trials) =>
        {
            if (await accounts.GetAccountByTokenAsync(BearerToken(http)) is not { } a) return Results.Unauthorized();
            var result = await trials.ReleaseAsync(a.Id, r.ReservationId);
            return Results.Ok(ToDto(result.Status, new TrialStatusDto { Success = result.Succeeded, Message = result.Error }));
        });

        // Plans (public) and plan requests (signed in or not).
        var store = app.MapGroup("/api/v1").RequireRateLimiting("account");

        store.MapGet("/plans", async (string? product, PlanService plans) =>
        {
            var list = await plans.GetActivePlansAsync(string.IsNullOrWhiteSpace(product) ? ProductCodes.InfoID : product.Trim().ToUpperInvariant());
            return list.Select(p => new PlanDto
            {
                Id = p.Id,
                Name = p.Name,
                Tier = p.Tier,
                Tagline = p.Tagline,
                Features = (p.FeaturesText ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                DurationMonths = p.DurationMonths,
                DefaultMachines = p.DefaultMachines,
                MaxCardsPerPeriod = p.MaxCardsPerPeriod,
                CardLimitPeriod = p.CardLimitPeriod,
                PriceText = p.PriceText,
            }).ToList();
        });

        store.MapPost("/requests", async (PlanRequestSubmission r, HttpContext http, CustomerAccountService accounts, LicenseRequestService requests) =>
        {
            // A token that doesn't resolve is treated as "not signed in", not an error.
            var signedIn = await accounts.GetAccountByTokenAsync(BearerToken(http));
            var (ok, error, id) = await requests.SubmitAsync(new SubmitRequestInput(
                r.PlanId, r.Machines, r.FullName, r.CompanyName, r.Email, r.Phone, r.Country, r.StateProvince,
                r.Message, r.MachineFingerprint, r.AppVersion), signedIn);
            return new PlanRequestResponse { Success = ok, Message = error, RequestId = id };
        });
    }

    private static T ToDto<T>(TrialStatus status, T dto) where T : TrialStatusDto
    {
        dto.CardLimit = status.Limit;
        dto.CardsUsed = status.Used;
        dto.CardsReserved = status.Reserved;
        dto.CardsRemaining = status.Remaining;
        return dto;
    }

    private static string? BearerToken(HttpContext http)
    {
        var header = http.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header["Bearer ".Length..].Trim() : null;
    }

    private static AccountResponse ToResponse(AccountResult result) => new()
    {
        Success = result.Succeeded,
        Message = result.Error,
        NeedsVerification = result.NeedsVerification,
        Token = result.Token,
        Account = result.Account is { } a && result.Token is not null ? ToProfile(a) : null,
    };

    private static AccountProfileDto ToProfile(CustomerAccount a) => new()
    {
        FullName = a.FullName,
        CompanyName = a.CompanyName,
        Email = a.Email,
        Phone = a.Phone,
        Country = a.Country,
    };
}
