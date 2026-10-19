using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace InfoID.AdminLicenseManager.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();

        var version = typeof(LoginView).Assembly.GetName().Version;
        VersionText.Text = version is null ? "" : $"Version {version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>Put the cursor in the Email box so staff can type straight away.</summary>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Dispatcher.UIThread.Post(() => EmailBox.Focus(), DispatcherPriority.Background);
    }
}
