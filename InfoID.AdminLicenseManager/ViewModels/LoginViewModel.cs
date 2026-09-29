using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Licensing.Server.Data;
using InfoID.Licensing.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace InfoID.AdminLicenseManager.ViewModels;

/// <summary>Sign-in, plus first-run creation of the initial Super Admin when the
/// database has no staff accounts yet. Also applies pending database migrations and
/// reports connection/secret problems in plain words before anyone can sign in.</summary>
public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly AuthService _auth;
    private readonly IDbContextFactory<LicenseDbContext> _dbFactory;
    private readonly LicenseSecretStore _secrets;

    public event Action? SignedIn;

    [ObservableProperty] private bool _isReady;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isFirstRun;
    [ObservableProperty] private string _fullName = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _confirmPassword = "";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string? _warning;

    public LoginViewModel(AuthService auth, IDbContextFactory<LicenseDbContext> dbFactory, LicenseSecretStore secrets)
    {
        _auth = auth;
        _dbFactory = dbFactory;
        _secrets = secrets;
    }

    public async Task InitializeAsync()
    {
        IsBusy = true;
        Error = null;
        try
        {
            await using (var db = await _dbFactory.CreateDbContextAsync())
            {
                await db.Database.MigrateAsync();
            }

            IsFirstRun = !await _auth.HasAnyAdminAsync();
            Warning = _secrets.IsConfigured
                ? null
                : $"No license secret is configured on this computer ({LicenseSecretStore.VariablePrefix}1). You can sign in and manage codes, but generating serial keys will fail.";
            IsReady = true;
        }
        catch (Exception ex)
        {
            Error = "Can't connect to the license database. Check the LicenseDb connection string in appsettings.json " +
                    "(or the INFOID_LICENSE_DB environment variable) and that PostgreSQL is running.\n\nDetails: " + ex.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task Retry() => InitializeAsync();

    [RelayCommand]
    private async Task SignIn()
    {
        if (IsBusy) return;
        IsBusy = true;
        Error = null;
        try
        {
            var result = await _auth.SignInAsync(Email, Password);
            Password = "";
            if (result.Succeeded) SignedIn?.Invoke();
            else Error = result.Error;
        }
        catch (Exception ex)
        {
            Error = "Sign-in failed: " + ex.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CreateAdmin()
    {
        if (IsBusy) return;
        if (Password != ConfirmPassword)
        {
            Error = "The passwords don't match.";
            return;
        }

        IsBusy = true;
        Error = null;
        try
        {
            var result = await _auth.CreateInitialSuperAdminAsync(FullName, Email, Password);
            Password = ConfirmPassword = "";
            if (result.Succeeded) SignedIn?.Invoke();
            else Error = result.Error;
        }
        catch (Exception ex)
        {
            Error = "Couldn't create the administrator: " + ex.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
