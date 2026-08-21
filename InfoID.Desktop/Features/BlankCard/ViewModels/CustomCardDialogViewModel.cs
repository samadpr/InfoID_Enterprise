using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Features.BlankCard.Models;

namespace InfoID.Desktop.Features.BlankCard.ViewModels;

/// <summary>
/// "Create my own card" dialog. Produces a <see cref="CustomCardSizeResult"/> when the
/// user confirms, or null on cancel. Hosted by <see cref="Core.Dialogs.IDialogService"/>;
/// this ViewModel has no knowledge of windows.
/// </summary>
public sealed partial class CustomCardDialogViewModel : DialogViewModelBase<CustomCardSizeResult>
{
    public CustomCardDialogViewModel()
    {
        _width = 85.725m;
        _height = 53.975m;
        _cornerRadius = 2.0m;
    }

    [ObservableProperty]
    private decimal _width;

    [ObservableProperty]
    private decimal _height;

    [ObservableProperty]
    private CardOrientation _orientation = CardOrientation.Landscape;

    public bool IsLandscape
    {
        get => Orientation == CardOrientation.Landscape;
        set { if (value) Orientation = CardOrientation.Landscape; }
    }

    public bool IsPortrait
    {
        get => Orientation == CardOrientation.Portrait;
        set { if (value) Orientation = CardOrientation.Portrait; }
    }

    [ObservableProperty]
    private decimal _cornerRadius;

    [ObservableProperty]
    private bool _saveToMyModels;

    [ObservableProperty]
    private string _modelName = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnOrientationChanged(CardOrientation value)
    {
        var shouldBeWidthLarger = value == CardOrientation.Landscape;
        var widthIsLarger = Width >= Height;

        if (shouldBeWidthLarger != widthIsLarger)
        {
            (Width, Height) = (Height, Width);
        }

        OnPropertyChanged(nameof(IsLandscape));
        OnPropertyChanged(nameof(IsPortrait));
    }

    [RelayCommand]
    private void Ok()
    {
        if (!Validate())
        {
            return;
        }

        RequestClose(new CustomCardSizeResult
        {
            Width = (double)Width,
            Height = (double)Height,
            Unit = SizeUnit.Millimeters,
            Orientation = Orientation,
            CornerRadius = (double)CornerRadius,
            SaveToMyModels = SaveToMyModels,
            ModelName = SaveToMyModels ? ModelName : null,
        });
    }

    [RelayCommand]
    private void Cancel() => RequestClose(null);

    private bool Validate()
    {
        if (Width <= 0 || Height <= 0)
        {
            ErrorMessage = "Width and height must be greater than zero.";
            return false;
        }

        if (CornerRadius < 0)
        {
            ErrorMessage = "Radius cannot be negative.";
            return false;
        }

        if (CornerRadius > Math.Min(Width, Height) / 2)
        {
            ErrorMessage = "Radius is too large for the given card size.";
            return false;
        }

        if (SaveToMyModels && string.IsNullOrWhiteSpace(ModelName))
        {
            ErrorMessage = "Enter a model name to save this card size.";
            return false;
        }

        ErrorMessage = null;
        return true;
    }
}
