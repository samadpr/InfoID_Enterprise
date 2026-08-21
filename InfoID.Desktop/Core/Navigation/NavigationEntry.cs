using InfoID.Desktop.ViewModels.Base;

namespace InfoID.Desktop.Core.Navigation;

/// <summary>One entry on the navigation back stack.</summary>
internal sealed record NavigationEntry(ViewModelBase ViewModel);
