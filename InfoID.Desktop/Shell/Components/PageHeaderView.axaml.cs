using Avalonia;
using Avalonia.Controls;
using System.Windows.Input;

namespace InfoID.Desktop.Shell.Components;

/// <summary>
/// Reusable "&lt; Back / Page Title" header used by every sub-page (Blank Card,
/// Templates, and future pages). Exposes plain AvaloniaProperties rather than sharing a
/// ViewModel type, since it is used from several unrelated feature ViewModels.
/// </summary>
public partial class PageHeaderView : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<PageHeaderView, string?>(nameof(Title));

    public static readonly StyledProperty<ICommand?> BackCommandProperty =
        AvaloniaProperty.Register<PageHeaderView, ICommand?>(nameof(BackCommand));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public ICommand? BackCommand
    {
        get => GetValue(BackCommandProperty);
        set => SetValue(BackCommandProperty, value);
    }

    public PageHeaderView()
    {
        InitializeComponent();
    }
}
