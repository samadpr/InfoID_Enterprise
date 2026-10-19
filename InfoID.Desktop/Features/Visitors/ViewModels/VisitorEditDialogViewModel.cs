using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Application.Dtos.VisitorModule;
using InfoID.Application.Interfaces;
using InfoID.Application.Services;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Features.Visitors.Services;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.Desktop.Features.Visitors.ViewModels;

/// <summary>An extra field (form question / file column) shown and editable in the dialog.</summary>
public sealed partial class VisitorExtraFieldViewModel : ObservableObject
{
    public VisitorExtraFieldViewModel(string name, string value)
    {
        Name = name;
        _value = value;
    }

    public string Name { get; }
    [ObservableProperty] private string _value;
}

/// <summary>
/// Add a visitor by hand, or correct one (typo in the name, wrong person to meet, replace
/// the photo). Uses the same rules as the import; the visitor ID never changes.
/// </summary>
public sealed partial class VisitorEditDialogViewModel : DialogViewModelBase<VisitorDto?>
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<VisitorEditDialogViewModel>();

    private readonly IServiceScopeFactory _scopes;
    private readonly IFilePickerService _filePicker;
    private readonly VisitorPhotoStore _photos;
    private readonly VisitorDto? _existing;

    /// <summary>A newly chosen photo file (not yet copied into InfoID).</summary>
    private string? _newPhotoFile;
    private bool _photoRemoved;

    public VisitorEditDialogViewModel(IServiceScopeFactory scopes, IFilePickerService filePicker, VisitorDto? existing, VisitorPhotoStore? photos = null)
    {
        _scopes = scopes;
        _filePicker = filePicker;
        _existing = existing;
        _photos = photos ?? new VisitorPhotoStore();

        if (existing is not null)
        {
            _fullName = existing.FullName;
            _phone = existing.Phone ?? string.Empty;
            _email = existing.Email ?? string.Empty;
            _company = existing.Company ?? string.Empty;
            _personToMeet = existing.PersonToMeet ?? string.Empty;
            _purpose = existing.Purpose ?? string.Empty;
            _visitDate = existing.VisitDate?.ToDateTime(TimeOnly.MinValue);
            foreach (var (name, value) in existing.ExtraFields.OrderBy(e => e.Key, StringComparer.CurrentCultureIgnoreCase))
                ExtraFields.Add(new VisitorExtraFieldViewModel(name, value));
            LoadPreview(_photos.GetFullPath(existing.PhotoPath));
        }
        else
        {
            _visitDate = DateTime.Today;
        }
    }

    public override string Title => IsNew ? "Add visitor" : $"Edit visitor {_existing!.VisitorCode}";
    public override (double Width, double Height)? PreferredSize => (720, 720);

    public bool IsNew => _existing is null;
    public string Heading => IsNew ? "Add visitor" : $"{_existing!.VisitorCode} · {_existing.FullName}";
    public string SourceText => _existing is null ? string.Empty : $"From {_existing.SourceName ?? _existing.Source.ToString()} · added {_existing.CreatedUtc.ToLocalTime():g}";

    [ObservableProperty] private string _fullName = string.Empty;
    [ObservableProperty] private string _phone = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _company = string.Empty;
    [ObservableProperty] private string _personToMeet = string.Empty;
    [ObservableProperty] private string _purpose = string.Empty;
    [ObservableProperty] private DateTime? _visitDate;

    public ObservableCollection<VisitorExtraFieldViewModel> ExtraFields { get; } = new();
    public bool HasExtraFields => ExtraFields.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPhoto))]
    private Bitmap? _photoPreview;

    public bool HasPhoto => PhotoPreview is not null;

    [ObservableProperty] private string? _errorText;
    [ObservableProperty] private string? _warningText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isBusy;

    [RelayCommand]
    private async Task ChoosePhoto()
    {
        var path = await _filePicker.PickImageFileAsync("Visitor photo");
        if (string.IsNullOrWhiteSpace(path)) return;

        var problem = VisitorPhotoStore.CheckImage(path);
        if (problem is not null)
        {
            ErrorText = problem;
            return;
        }
        ErrorText = null;
        _newPhotoFile = path;
        _photoRemoved = false;
        LoadPreview(path);
    }

    [RelayCommand]
    private void RemovePhoto()
    {
        _newPhotoFile = null;
        _photoRemoved = true;
        PhotoPreview = null;
    }

    private void LoadPreview(string? path)
    {
        if (path is null)
        {
            PhotoPreview = null;
            return;
        }
        try
        {
            using var stream = File.OpenRead(path);
            PhotoPreview = Bitmap.DecodeToWidth(stream, 240);
        }
        catch (Exception ex)
        {
            Log.Warn("Visitor photo preview couldn't be loaded", ex);
            PhotoPreview = null;
        }
    }

    private VisitorInput BuildInput(string? photoPath)
    {
        var input = new VisitorInput
        {
            FullName = FullName,
            Phone = Phone,
            Email = Email,
            Company = Company,
            PersonToMeet = PersonToMeet,
            Purpose = Purpose,
            VisitDate = VisitDate is { } d ? DateOnly.FromDateTime(d) : null,
            PhotoPath = photoPath,
        };
        foreach (var extra in ExtraFields) input.ExtraFields[extra.Name] = extra.Value;
        return input;
    }

    private bool CanSave() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        // Check first with the photo the visitor will have, so the "no photo" warning is right.
        var willHavePhoto = _newPhotoFile ?? (_photoRemoved ? null : _existing?.PhotoPath);
        var issues = VisitorValidation.Validate(BuildInput(willHavePhoto), DateOnly.FromDateTime(DateTime.Today));
        var errors = issues.Where(i => i.Severity == VisitorIssueSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            ErrorText = string.Join(Environment.NewLine, errors.Select(e => $"{e.Field}: {e.Message}"));
            WarningText = null;
            return;
        }
        ErrorText = null;

        IsBusy = true;
        string? copiedPhoto = null;
        try
        {
            if (_newPhotoFile is not null) copiedPhoto = await _photos.ImportAsync(_newPhotoFile);
            var photoPath = copiedPhoto ?? (_photoRemoved ? null : _existing?.PhotoPath);

            using var scope = _scopes.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IVisitorService>();
            VisitorDto saved;
            if (_existing is null)
            {
                saved = await service.AddAsync(BuildInput(photoPath));
            }
            else
            {
                var (updated, replacedPhoto) = await service.UpdateAsync(_existing.Id, BuildInput(photoPath));
                if (replacedPhoto is not null) _photos.Delete(replacedPhoto);
                saved = updated;
            }
            RequestClose(saved);
        }
        catch (Exception ex)
        {
            if (copiedPhoto is not null) _photos.Delete(copiedPhoto);
            Log.Warn("Visitor couldn't be saved", ex);
            ErrorText = ex is InvalidOperationException ? ex.Message : "The visitor couldn't be saved: " + ex.GetBaseException().Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => RequestClose(null);
}
