using Avalonia.Controls;
using InfoID.Desktop.Shell.ViewModels;

namespace InfoID.Desktop.Shell.Views;

/// <summary>The application's single top-level window. Content is entirely driven by
/// navigation/DataContext -- this code-behind stays framework-only, per MVVM. The one
/// exception is Full Screen (Part 24/82): only a View can touch WindowState, so this
/// just reacts to a request event from the ViewModel instead of the ViewModel reaching
/// into the Window directly.</summary>
public partial class ShellView : Window
{
    public ShellView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is ShellViewModel vm)
            {
                vm.ToggleFullScreenRequested += (_, _) =>
                    WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
            }
        };
    }
}
