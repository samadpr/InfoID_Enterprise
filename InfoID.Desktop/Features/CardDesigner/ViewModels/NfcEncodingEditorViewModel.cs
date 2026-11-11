using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.CardEncoding.Nfc;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Desktop.Features.Rfid.Services;

namespace InfoID.Desktop.Features.CardDesigner.ViewModels;

/// <summary>The record the Encoding panel previews.</summary>
public sealed record NfcPreviewRecord(string Label, IReadOnlyDictionary<string, string> Fields, bool IsSample);

public sealed record NfcTagChoice(NfcTagModel Model, string Label);

public sealed record NfcIssueLine(NfcIssueSeverity Severity, string Message)
{
    public bool IsError => Severity == NfcIssueSeverity.Error;
    public bool IsWarning => Severity == NfcIssueSeverity.Warning;
    public bool IsInfo => Severity == NfcIssueSeverity.Info;
}

/// <summary>
/// State of the Properties panel's CARD ENCODING section for one design tab. Wraps the
/// design's NfcEncodingSettings (a plain DTO, like the other document settings), reports
/// every change through the tab's NotifyDocumentChanged/MarkDirty pipeline (so autosave and
/// the unsaved dot work as usual), and keeps a live preview of the selected record.
/// Configuring and previewing is allowed in every edition and in trial; writing is gated
/// separately (Step 4).
/// </summary>
public sealed partial class NfcEncodingEditorViewModel : ObservableObject
{
    private readonly NfcEncodingSettings _settings;
    private readonly Func<NfcPreviewRecord> _previewSource;
    private readonly NfcContentBuilder _builder;
    private readonly Action _changed;
    private string? _urlFieldToInsert;
    private string? _textFieldToInsert;

    public NfcEncodingEditorViewModel(NfcEncodingSettings settings, ObservableCollection<string> columns,
        Func<NfcPreviewRecord> previewSource, NfcContentBuilder builder, Action changed)
    {
        _settings = settings;
        _previewSource = previewSource;
        _builder = builder;
        _changed = changed;
        Columns = columns;
        TagChoices = NfcTagModels.All
            .Select(m => new NfcTagChoice(m, $"{NfcTagModels.DisplayName(m)} · {NfcTagModels.CapacityBytes(m)} bytes"
                                             + (m == NfcTagModel.Ntag216 ? " (recommended)" : "")))
            .ToList();
        RefreshPreview();
    }

    /// <summary>Columns of the connected data (the tab's own list), for "Insert field".</summary>
    public ObservableCollection<string> Columns { get; }
    public IReadOnlyList<NfcTagChoice> TagChoices { get; }
    public ObservableCollection<NfcIssueLine> Issues { get; } = new();

    // ------------------------------------------------------------------ settings ----

    public bool Enabled
    {
        get => _settings.Enabled;
        set => Update(_settings.Enabled, value, v => _settings.Enabled = v);
    }

    public NfcContentMode Mode
    {
        get => _settings.Mode;
        set
        {
            if (Update(_settings.Mode, value, v => _settings.Mode = v))
            {
                OnPropertyChanged(nameof(ShowUrl));
                OnPropertyChanged(nameof(ShowText));
            }
        }
    }

    public bool ShowUrl => Mode is NfcContentMode.Url or NfcContentMode.UrlAndText;
    public bool ShowText => Mode is NfcContentMode.Text or NfcContentMode.UrlAndText;

    public string UrlTemplate
    {
        get => _settings.UrlTemplate;
        set => Update(_settings.UrlTemplate, value ?? string.Empty, v => _settings.UrlTemplate = v);
    }

    public string TextTemplate
    {
        get => _settings.TextTemplate;
        set => Update(_settings.TextTemplate, value ?? string.Empty, v => _settings.TextTemplate = v);
    }

    public NfcTagChoice? SelectedTagChoice
    {
        get => TagChoices.FirstOrDefault(c => c.Model == _settings.SizeCheckTag);
        set
        {
            if (value is not null) Update(_settings.SizeCheckTag, value.Model, v => _settings.SizeCheckTag = v);
        }
    }

    /// <summary>"Insert field" for the link: appends {{Column}}, then clears the picker.</summary>
    public string? UrlFieldToInsert
    {
        get => _urlFieldToInsert;
        set
        {
            if (!SetProperty(ref _urlFieldToInsert, value) || string.IsNullOrEmpty(value)) return;
            UrlTemplate = (UrlTemplate ?? string.Empty) + "{{" + value + "}}";
            Dispatcher.UIThread.Post(() => UrlFieldToInsert = null);
        }
    }

    /// <summary>"Insert field" for the text: appends {{Column}}, then clears the picker.</summary>
    public string? TextFieldToInsert
    {
        get => _textFieldToInsert;
        set
        {
            if (!SetProperty(ref _textFieldToInsert, value) || string.IsNullOrEmpty(value)) return;
            TextTemplate = (TextTemplate ?? string.Empty) + "{{" + value + "}}";
            Dispatcher.UIThread.Post(() => TextFieldToInsert = null);
        }
    }

    [RelayCommand]
    private void ResetUrlTemplate() => UrlTemplate = NfcEncodingSettings.DefaultUrlTemplate;

    // ------------------------------------------------------------------- preview ----

    [ObservableProperty] private string _previewLabel = string.Empty;
    [ObservableProperty] private string? _previewUrl;
    [ObservableProperty] private string? _previewText;
    [ObservableProperty] private string _sizeText = string.Empty;
    [ObservableProperty] private double _sizePercent;
    [ObservableProperty] private bool _isOverCapacity;
    [ObservableProperty] private string _tagFitText = string.Empty;
    [ObservableProperty] private bool _hasIssues;

    public bool HasPreviewUrl => !string.IsNullOrEmpty(PreviewUrl);
    public bool HasPreviewText => !string.IsNullOrEmpty(PreviewText);
    partial void OnPreviewUrlChanged(string? value) => OnPropertyChanged(nameof(HasPreviewUrl));
    partial void OnPreviewTextChanged(string? value) => OnPropertyChanged(nameof(HasPreviewText));

    /// <summary>Rebuilds the preview for the current record (also called by the tab when the
    /// selected row, the connected data or the sample record changes).</summary>
    public void RefreshPreview()
    {
        var source = _previewSource();
        var content = _builder.Build(_settings, source.Fields);

        PreviewLabel = source.Label;
        PreviewUrl = content.Url;
        PreviewText = content.Text;

        var capacity = NfcTagModels.CapacityBytes(_settings.SizeCheckTag);
        SizeText = $"{content.SizeBytes} of {capacity} bytes ({NfcTagModels.DisplayName(_settings.SizeCheckTag)})";
        SizePercent = capacity == 0 ? 0 : Math.Min(100, content.SizeBytes * 100.0 / capacity);
        IsOverCapacity = content.SizeBytes > capacity;
        TagFitText = string.Join("   ",
            new[] { NfcTagModel.Ntag213, NfcTagModel.Ntag215, NfcTagModel.Ntag216 }
                .Select(m => $"{NfcTagModels.DisplayName(m)} {(content.FitsOn(m) ? "✓" : "✗")}"));

        Issues.Clear();
        if (source.IsSample)
            Issues.Add(new NfcIssueLine(NfcIssueSeverity.Info,
                "No data connected: previewing a sample record. Connect your data to preview real values."));
        foreach (var issue in content.Issues)
            Issues.Add(new NfcIssueLine(issue.Severity, issue.Message));
        HasIssues = Issues.Count > 0;
    }

    private bool Update<T>(T current, T value, Action<T> apply, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return false;
        apply(value);
        OnPropertyChanged(propertyName);
        _changed();
        RefreshPreview();
        return true;
    }
}
