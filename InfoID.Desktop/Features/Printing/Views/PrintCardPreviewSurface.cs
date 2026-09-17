using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Desktop.Features.CardDesigner.Services;
using InfoID.Desktop.Features.Printing.ViewModels;

namespace InfoID.Desktop.Features.Printing.Views;

/// <summary>
/// The Print dialog's "Color" preview -- live vector rendering via ThumbnailRenderer,
/// same technique as CardDesigner's own CardPreviewSurface (used by Print Preview), just
/// bound to PrintDialogViewModel instead. Kept as its own small control rather than
/// generalizing CardPreviewSurface to accept either ViewModel type, since that would mean
/// reaching across from Card Designer into the Print module (or vice versa) for a type
/// that has no reason to be shared beyond this one binding surface.
/// </summary>
public sealed class PrintCardPreviewSurface : Control
{
    public static readonly StyledProperty<PrintDialogViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<PrintCardPreviewSurface, PrintDialogViewModel?>(nameof(ViewModel));

    public PrintDialogViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly StyledProperty<CardSide> SideProperty =
        AvaloniaProperty.Register<PrintCardPreviewSurface, CardSide>(nameof(Side));

    public CardSide Side
    {
        get => GetValue(SideProperty);
        set => SetValue(SideProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ViewModelProperty) return;

        if (change.OldValue is PrintDialogViewModel oldVm) oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        if (change.NewValue is PrintDialogViewModel newVm) newVm.PropertyChanged += OnViewModelPropertyChanged;
        InvalidateVisual();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var vm = ViewModel;
        if (vm is null) return;

        var options = new ThumbnailRenderer.RenderOptions
        {
            ResolveText = vm.ResolveText,
            ResolveField = vm.ResolveField,
            IsVisibleNow = vm.IsVisibleNow,
            ResolveAssetPath = vm.ResolveAssetPath,
        };
        ThumbnailRenderer.Render(context, vm.Document, Side, new Rect(Bounds.Size), options);
    }
}
