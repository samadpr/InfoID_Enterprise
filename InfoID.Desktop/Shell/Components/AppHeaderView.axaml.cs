using Avalonia.Controls;

namespace InfoID.Desktop.Shell.Components;

/// <summary>Top application header: branding, primary menu, theme toggle. DataContext is
/// inherited from the shell (ShellViewModel) -- this control never sets its own.</summary>
public partial class AppHeaderView : UserControl
{
    public AppHeaderView()
    {
        InitializeComponent();
    }
}
