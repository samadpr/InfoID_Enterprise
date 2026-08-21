using Avalonia.Controls;

namespace InfoID.Desktop.Shell.Views;

/// <summary>The application's single top-level window. Content is entirely driven by
/// navigation/DataContext -- this code-behind stays framework-only, per MVVM.</summary>
public partial class ShellView : Window
{
    public ShellView()
    {
        InitializeComponent();
    }
}
