using CommunityToolkit.Mvvm.ComponentModel;

namespace InfoID.Desktop.ViewModels.Base;

/// <summary>
/// Base class for every ViewModel in InfoID.Desktop. Wraps CommunityToolkit.Mvvm's
/// ObservableObject so all ViewModels get INotifyPropertyChanged + [ObservableProperty]
/// support for free. Feature ViewModels (Welcome, BlankCard, Templates, CardDesigner, ...)
/// should derive from this rather than implementing INotifyPropertyChanged manually.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
    /// <summary>
    /// Called by the navigation service right after the ViewModel becomes the active
    /// workspace. Override to load data (recent cards, catalogs, etc.). Optional
    /// navigation parameter allows passing context between pages (e.g. selected format).
    /// </summary>
    public virtual void OnNavigatedTo(object? parameter)
    {
    }

    /// <summary>
    /// Called right before navigating away from this ViewModel. Override to persist
    /// draft state or release resources.
    /// </summary>
    public virtual void OnNavigatedFrom()
    {
    }
}
