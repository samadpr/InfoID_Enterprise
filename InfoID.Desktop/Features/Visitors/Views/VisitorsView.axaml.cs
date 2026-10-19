using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using InfoID.Desktop.Features.Visitors.ViewModels;

namespace InfoID.Desktop.Features.Visitors.Views;

public partial class VisitorsView : UserControl
{
    public VisitorsView()
    {
        InitializeComponent();
        // Double-click a visitor to edit it (same as the Edit button / F2).
        VisitorsGrid.DoubleTapped += OnGridDoubleTapped;
    }

    private void OnGridDoubleTapped(object? sender, TappedEventArgs e)
    {
        // Double-clicking the tick box only ticks/unticks.
        if (e.Source is Avalonia.Visual visual && visual.FindAncestorOfType<CheckBox>(includeSelf: true) is not null) return;
        if (DataContext is VisitorsViewModel vm && vm.EditVisitorCommand.CanExecute(null))
            vm.EditVisitorCommand.Execute(null);
    }
}
