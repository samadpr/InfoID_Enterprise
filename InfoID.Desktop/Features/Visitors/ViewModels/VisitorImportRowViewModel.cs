using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using InfoID.Application.Dtos.VisitorModule;
using InfoID.Application.Services;
using InfoID.Desktop.Features.Visitors.Services;

namespace InfoID.Desktop.Features.Visitors.ViewModels;

public enum VisitorImportRowStatus { Ready, Warning, Error, AlreadyImported, DuplicateInFile }

/// <summary>
/// One row of a bulk-upload file in the import review. Every text field can be corrected
/// in the grid; the row is checked again after each change. "Already imported" and
/// "repeated in this file" rows are never imported, whatever their content.
/// </summary>
public sealed partial class VisitorImportRowViewModel : ObservableObject
{
    private readonly DateOnly _today;

    public VisitorImportRowViewModel(int rowNumber, DateOnly today)
    {
        RowNumber = rowNumber;
        _today = today;
    }

    /// <summary>Row number as in the file (header = row 1), for "row 12 has an error".</summary>
    public int RowNumber { get; }

    [ObservableProperty] private string _fullName = string.Empty;
    [ObservableProperty] private string _phone = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _company = string.Empty;
    [ObservableProperty] private string _personToMeet = string.Empty;
    [ObservableProperty] private string _purpose = string.Empty;
    [ObservableProperty] private string _visitDateText = string.Empty;

    /// <summary>The photo as written in the file (file name, path or link).</summary>
    [ObservableProperty] private string _photoText = string.Empty;
    /// <summary>The photo file found for this row; null = no usable photo.</summary>
    [ObservableProperty] private string? _photoFile;
    /// <summary>Why the photo can't be used (not found, not an image, a link…).</summary>
    [ObservableProperty] private string? _photoProblem;

    public string? ResponseKey { get; set; }
    public Dictionary<string, string> ExtraFields { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ticked rows are imported (unticking skips a row without deleting it from the file).</summary>
    [ObservableProperty] private bool _include = true;

    [ObservableProperty] private VisitorImportRowStatus _status;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private string _issuesText = string.Empty;

    public bool IsAlreadyImported { get; private set; }
    public bool IsDuplicateInFile { get; private set; }
    public string SourceKey { get; private set; } = string.Empty;

    public bool HasPhoto => PhotoFile is not null;
    public bool IsError => Status == VisitorImportRowStatus.Error;
    public bool IsWarning => Status == VisitorImportRowStatus.Warning;
    public bool IsSkipped => Status is VisitorImportRowStatus.AlreadyImported or VisitorImportRowStatus.DuplicateInFile;
    public bool CanImport => Include && Status is VisitorImportRowStatus.Ready or VisitorImportRowStatus.Warning;

    /// <summary>Raised after a field edit re-checked the row, so the dialog can update its counts.</summary>
    public event EventHandler? Changed;

    private bool _suspend;

    /// <summary>Sets values without re-checking after each one (used while building rows).</summary>
    public void BeginInit() => _suspend = true;

    public void EndInit()
    {
        _suspend = false;
        SourceKey = VisitorColumnMatcher.BuildSourceKey(ResponseKey, FullName, Phone, Email, VisitDateText);
    }

    public void MarkDuplicate(bool alreadyImported, bool duplicateInFile)
    {
        IsAlreadyImported = alreadyImported;
        IsDuplicateInFile = duplicateInFile;
        Validate();
    }

    partial void OnFullNameChanged(string value) => Edited();
    partial void OnPhoneChanged(string value) => Edited();
    partial void OnEmailChanged(string value) => Edited();
    partial void OnCompanyChanged(string value) => Edited();
    partial void OnPersonToMeetChanged(string value) => Edited();
    partial void OnPurposeChanged(string value) => Edited();
    partial void OnVisitDateTextChanged(string value) { OnPropertyChanged(nameof(VisitDateMeaning)); Edited(); }
    partial void OnPhotoFileChanged(string? value) { OnPropertyChanged(nameof(HasPhoto)); Edited(); }
    partial void OnPhotoProblemChanged(string? value) => Edited();
    partial void OnIncludeChanged(bool value) { OnPropertyChanged(nameof(CanImport)); Changed?.Invoke(this, EventArgs.Empty); }

    partial void OnStatusChanged(VisitorImportRowStatus value)
    {
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(IsWarning));
        OnPropertyChanged(nameof(IsSkipped));
        OnPropertyChanged(nameof(CanImport));
    }

    private void Edited()
    {
        if (_suspend) return;
        // The duplicate key is fixed when the row is read (so correcting a typo doesn't
        // turn an already-imported row into a "new" one).
        Validate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Re-checks the row with the shared visitor rules.</summary>
    public void Validate()
    {
        if (IsAlreadyImported || IsDuplicateInFile)
        {
            Status = IsAlreadyImported ? VisitorImportRowStatus.AlreadyImported : VisitorImportRowStatus.DuplicateInFile;
            StatusText = IsAlreadyImported ? "Already imported" : "Repeated in this file";
            IssuesText = IsAlreadyImported
                ? "This registration was imported before; it's skipped."
                : "The same registration appears earlier in this file; it's skipped.";
            return;
        }

        var input = ToInput(out var dateProblem);
        var issues = VisitorValidation.Validate(input, _today).ToList();

        // Photo: the file's own problem replaces the generic "no photo" warning.
        if (PhotoProblem is not null)
        {
            issues.RemoveAll(i => i.Field == VisitorValidation.FieldPhoto);
            issues.Add(new VisitorIssue(VisitorValidation.FieldPhoto, PhotoProblem, VisitorIssueSeverity.Warning));
        }
        if (dateProblem is not null)
        {
            issues.RemoveAll(i => i.Field == VisitorValidation.FieldVisitDate);
            issues.Add(new VisitorIssue(VisitorValidation.FieldVisitDate, dateProblem, VisitorIssueSeverity.Error));
        }

        var errors = issues.Where(i => i.Severity == VisitorIssueSeverity.Error).ToList();
        var warnings = issues.Where(i => i.Severity == VisitorIssueSeverity.Warning).ToList();
        Status = errors.Count > 0 ? VisitorImportRowStatus.Error
               : warnings.Count > 0 ? VisitorImportRowStatus.Warning
               : VisitorImportRowStatus.Ready;
        StatusText = Status switch
        {
            VisitorImportRowStatus.Error => errors.Count == 1 ? "1 error" : $"{errors.Count} errors",
            VisitorImportRowStatus.Warning => warnings.Count == 1 ? "Ready · 1 warning" : $"Ready · {warnings.Count} warnings",
            _ => "Ready",
        };
        IssuesText = string.Join(Environment.NewLine, errors.Concat(warnings).Select(i => $"{i.Field}: {i.Message}"));
    }

    /// <summary>The row as visitor input; <paramref name="dateProblem"/> is set when the
    /// visit date text isn't a date.</summary>
    public VisitorImportItem ToInput(out string? dateProblem)
    {
        dateProblem = null;
        DateOnly? date = null;
        if (!string.IsNullOrWhiteSpace(VisitDateText))
        {
            if (VisitorColumnMatcher.TryParseDate(VisitDateText, out var parsed)) date = parsed;
            else dateProblem = $"\"{VisitDateText.Trim()}\" isn't a date InfoID can read (use e.g. 2026-10-15).";
        }

        var item = new VisitorImportItem
        {
            FullName = FullName,
            Phone = Phone,
            Email = Email,
            Company = Company,
            PersonToMeet = PersonToMeet,
            Purpose = Purpose,
            VisitDate = date,
            // Validation only needs to know whether there is a photo; the real reference is
            // set when the photo is copied into InfoID.
            PhotoPath = PhotoFile,
            SourceKey = SourceKey,
        };
        foreach (var (key, value) in ExtraFields) item.ExtraFields[key] = value;
        return item;
    }

    /// <summary>The visit date as understood, for the grid's tooltip ("15 Oct 2026").</summary>
    public string VisitDateMeaning =>
        VisitorColumnMatcher.TryParseDate(VisitDateText, out var d) ? d.ToString("dddd, d MMMM yyyy") : string.Empty;
}
