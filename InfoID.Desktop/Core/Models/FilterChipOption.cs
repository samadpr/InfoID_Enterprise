using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace InfoID.Desktop.Core.Models;

/// <summary>
/// A single toggleable filter chip (used for orientation/format/category filters on the
/// Blank Card and Templates pages). Kept generic and reusable rather than re-implemented
/// per page. The owning ViewModel passes a callback that re-runs filtering whenever the
/// chip's selection state changes.
/// </summary>
public sealed partial class FilterChipOption : ObservableObject
{
    private readonly Action? _onChanged;

    public FilterChipOption(string label, bool isSelected = false, Action? onChanged = null)
    {
        Label = label;
        _isSelected = isSelected;
        _onChanged = onChanged;
    }

    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _onChanged?.Invoke();
}
