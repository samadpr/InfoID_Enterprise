using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Desktop.Features.CardDesigner.Services;
using InfoID.Desktop.Features.CardDesigner.ViewModels;

namespace InfoID.Desktop.Features.CardDesigner.Views.Controls;

/// <summary>
/// Non-interactive card render surface for Print Preview -- one instance per side
/// (Front/Back), redrawing via ThumbnailRenderer whenever the bound PrintPreviewViewModel
/// changes (selected sample record, bleed/safe-zone toggle, ...). Deliberately not
/// CardCanvasView: no selection/drag/zoom/pan is needed here, and reusing
/// ThumbnailRenderer's already-accurate text/shape drawing plus its new bleed/safe-zone
/// and data-binding options (added specifically for this) avoids a third rendering
/// implementation.
/// </summary>
public sealed class CardPreviewSurface : Control
{
    public static readonly StyledProperty<PrintPreviewViewModel?> ViewModelProperty =
        AvaloniaProperty.Register<CardPreviewSurface, PrintPreviewViewModel?>(nameof(ViewModel));

    public PrintPreviewViewModel? ViewModel
    {
        get => GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly StyledProperty<CardSide> SideProperty =
        AvaloniaProperty.Register<CardPreviewSurface, CardSide>(nameof(Side));

    public CardSide Side
    {
        get => GetValue(SideProperty);
        set => SetValue(SideProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ViewModelProperty) return;

        if (change.OldValue is PrintPreviewViewModel oldVm) oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        if (change.NewValue is PrintPreviewViewModel newVm) newVm.PropertyChanged += OnViewModelPropertyChanged;
        InvalidateVisual();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    private void OnTrialModeChanged(object? sender, System.EventArgs e) => InvalidateVisual();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (InfoID.Desktop.Features.Trial.Services.TrialModeState.Current is { } trial) trial.Changed += OnTrialModeChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (InfoID.Desktop.Features.Trial.Services.TrialModeState.Current is { } trial) trial.Changed -= OnTrialModeChanged;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var vm = ViewModel;
        if (vm is null) return;

        var options = new ThumbnailRenderer.RenderOptions
        {
            ResolveText = vm.ResolveText,
            ResolveField = vm.ResolveField,
            ResolveCodeValue = vm.ResolveCodeValue,
            IsVisibleNow = vm.IsVisibleNow,
            ShowBleedAndSafeZone = vm.ShowBleedAndSafeZone,
            ResolveAssetPath = vm.ResolveAssetPath,
            // Trial mode (signed in, not activated): preview shows the trial watermark.
            TrialWatermark = InfoID.Desktop.Features.Trial.Services.TrialModeState.Current?.IsTrialMode == true,
        };
        ThumbnailRenderer.Render(context, vm.Document, Side, new Rect(Bounds.Size), options);
    }
}
