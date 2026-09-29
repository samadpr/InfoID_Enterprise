using Avalonia.Controls;
using Avalonia.Controls.Templates;
using InfoID.AdminLicenseManager.ViewModels;

namespace InfoID.AdminLicenseManager;

/// <summary>"...ViewModels.FooViewModel" -> "...Views.FooView", same convention as
/// InfoID.Desktop.</summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? param)
    {
        if (param is null) return new TextBlock { Text = "(no view model)" };

        var viewName = param.GetType().FullName!
            .Replace(".ViewModels.", ".Views.")
            .Replace("ViewModel", "View");

        var type = param.GetType().Assembly.GetType(viewName);
        if (type is not null && Activator.CreateInstance(type) is Control control)
        {
            control.DataContext = param;
            return control;
        }
        return new TextBlock { Text = "View not found: " + viewName };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
