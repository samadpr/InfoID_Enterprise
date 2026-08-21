using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Features.BlankCard.Models;
using InfoID.Desktop.Features.Templates.Models;
using InfoID.Desktop.Features.Welcome.ViewModels;
using InfoID.Desktop.ViewModels.Base;

namespace InfoID.Desktop.Features.CardDesigner.ViewModels;

/// <summary>
/// Placeholder workspace shown after selecting a blank format, a custom size or a
/// template. This is intentionally minimal today -- it only records what the user
/// chose and displays a summary -- but it is the exact extension point the real
/// designer (canvas, layers, tools, properties panel...) will be built into. See
/// Features/CardDesigner/{Views,ViewModels,Models,Services,Components,Tools} for the
/// folders reserved for that work.
/// </summary>
public sealed partial class CardDesignerViewModel : DocumentViewModelBase
{
    private readonly INavigationService _navigationService;

    public CardDesignerViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
        Title = "New card";
    }

    [ObservableProperty]
    private string _sourceDescription = "Blank card";

    [ObservableProperty]
    private string _sizeDescription = "85.6 x 54.0 mm";

    [ObservableProperty]
    private string _orientationDescription = "Landscape";

    public override void OnNavigatedTo(object? parameter)
    {
        switch (parameter)
        {
            case CardFormatOption format:
                Title = format.Name;
                SourceDescription = $"Blank card -- {format.Name}";
                SizeDescription = $"{format.WidthMm:0.###} x {format.HeightMm:0.###} mm ({format.CardSizeName})";
                OrientationDescription = format.Orientation.ToString();
                break;

            case CustomCardSizeResult custom:
                Title = "Custom card";
                SourceDescription = "Blank card -- custom size";
                SizeDescription = $"{custom.Width:0.###} x {custom.Height:0.###} mm, radius {custom.CornerRadius:0.###} mm";
                OrientationDescription = custom.Orientation.ToString();
                break;

            case TemplateCatalogItem template:
                Title = template.Name;
                SourceDescription = $"Template -- {template.Category}";
                SizeDescription = template.Format;
                OrientationDescription = template.Orientation.ToString();
                break;
        }
    }

    [RelayCommand]
    private void GoBack() => _navigationService.GoBack();

    [RelayCommand]
    private void GoHome() => _navigationService.NavigateToRoot<WelcomeViewModel>();
}
