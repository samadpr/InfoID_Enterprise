using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using InfoID.Desktop.ViewModels.Base;

namespace InfoID.Desktop;

/// <summary>
/// Resolves a View for a given ViewModel by naming convention:
/// "...ViewModels.FooViewModel" -&gt; "...Views.FooView". Used both as the shell's
/// global DataTemplate (so navigating to a new ViewModel automatically renders the
/// right page) and by <see cref="Core.Dialogs.DialogService"/> to host dialog content.
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? param)
    {
        if (param is null)
        {
            return new TextBlock { Text = "(no view model)" };
        }

        var viewModelName = param.GetType().FullName!;
        var viewName = viewModelName
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
