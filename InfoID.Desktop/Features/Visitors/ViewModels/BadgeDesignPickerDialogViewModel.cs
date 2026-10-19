using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Features.CardDesigner.Models;

namespace InfoID.Desktop.Features.Visitors.ViewModels;

/// <summary>The design chosen for visitor badges; Remember = use it every time.</summary>
public sealed record BadgeDesignChoice(long TemplateId, string Name, bool Remember);

/// <summary>One saved design in the picker.</summary>
public sealed class BadgeDesignOption
{
    private readonly Func<long, string?> _thumbnailPath;
    private Bitmap? _thumbnail;
    private bool _loaded;

    public BadgeDesignOption(RecentDesignSummary summary, Func<long, string?> thumbnailPath)
    {
        Summary = summary;
        _thumbnailPath = thumbnailPath;
    }

    public RecentDesignSummary Summary { get; }
    public long TemplateId => Summary.TemplateId;
    public string Name => Summary.Name;
    public string Details => $"{Summary.CardFormatLabel} · changed {Summary.ModifiedDate.ToLocalTime().ToString("dd MMM yyyy", CultureInfo.CurrentCulture)}";

    public Bitmap? Thumbnail
    {
        get
        {
            if (_loaded) return _thumbnail;
            _loaded = true;
            try
            {
                var path = _thumbnailPath(TemplateId);
                if (path is not null && File.Exists(path))
                {
                    using var stream = File.OpenRead(path);
                    _thumbnail = Bitmap.DecodeToWidth(stream, 120);
                }
            }
            catch (Exception ex)
            {
                InfoID.Shared.Logging.AppLog.For<BadgeDesignOption>().Warn("Design thumbnail couldn't be loaded", ex);
            }
            return _thumbnail;
        }
    }
}

/// <summary>Choose which saved design is used for visitor badges.</summary>
public sealed partial class BadgeDesignPickerDialogViewModel : DialogViewModelBase<BadgeDesignChoice?>
{
    public BadgeDesignPickerDialogViewModel(IReadOnlyList<RecentDesignSummary> designs, long? currentId, Func<long, string?>? thumbnailPath = null)
    {
        var resolve = thumbnailPath ?? (_ => null);
        Designs = designs.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(d => new BadgeDesignOption(d, resolve)).ToList();
        _selectedDesign = Designs.FirstOrDefault(d => d.TemplateId == currentId)
                          ?? Designs.FirstOrDefault(d => d.Name.Contains("visitor", StringComparison.OrdinalIgnoreCase))
                          ?? Designs.FirstOrDefault();
    }

    public override string Title => "Visitor badge design";
    public override (double Width, double Height)? PreferredSize => (640, 620);

    public IReadOnlyList<BadgeDesignOption> Designs { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChooseCommand))]
    private BadgeDesignOption? _selectedDesign;

    /// <summary>Use this design for every visitor badge (can be changed any time).</summary>
    [ObservableProperty] private bool _remember = true;

    private bool CanChoose() => SelectedDesign is not null;

    [RelayCommand(CanExecute = nameof(CanChoose))]
    private void Choose()
    {
        if (SelectedDesign is { } design) RequestClose(new BadgeDesignChoice(design.TemplateId, design.Name, Remember));
    }

    [RelayCommand]
    private void Cancel() => RequestClose(null);
}
