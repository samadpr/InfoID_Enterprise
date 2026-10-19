using System;
using System.Globalization;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using InfoID.Application.Dtos.VisitorModule;
using InfoID.Desktop.Features.Visitors.Services;

namespace InfoID.Desktop.Features.Visitors.ViewModels;

/// <summary>One visitor in the Visitors list.</summary>
public sealed partial class VisitorRowViewModel : ObservableObject
{
    private readonly VisitorPhotoStore _photos;
    private Bitmap? _thumbnail;
    private bool _thumbnailLoaded;

    public VisitorRowViewModel(VisitorDto dto, VisitorPhotoStore photos)
    {
        Dto = dto;
        _photos = photos;
    }

    public VisitorDto Dto { get; }

    /// <summary>Ticked for Print badges / Delete.</summary>
    [ObservableProperty] private bool _isChecked;

    public long Id => Dto.Id;
    public string VisitorCode => Dto.VisitorCode;
    public string FullName => Dto.FullName;
    public string Company => Dto.Company ?? string.Empty;
    public string PersonToMeet => Dto.PersonToMeet ?? string.Empty;
    public string Purpose => Dto.Purpose ?? string.Empty;
    public DateOnly SortDate => Dto.VisitDate ?? DateOnly.FromDateTime(Dto.CreatedUtc.ToLocalTime());
    public string VisitDateText => Dto.VisitDate?.ToString("dd MMM yyyy", CultureInfo.CurrentCulture) ?? string.Empty;

    public string ContactText => (Dto.Phone, Dto.Email) switch
    {
        ({ Length: > 0 } phone, { Length: > 0 } email) => $"{phone} · {email}",
        ({ Length: > 0 } phone, _) => phone,
        (_, { Length: > 0 } email) => email,
        _ => string.Empty,
    };

    public bool IsPrinted => Dto.IsPrinted;
    public string StatusText => Dto.IsPrinted
        ? Dto.PrintCount == 1
            ? $"Printed {Dto.LastPrintedUtc?.ToLocalTime().ToString("dd MMM", CultureInfo.CurrentCulture)}"
            : $"Printed ×{Dto.PrintCount}"
        : "Not printed";
    public string StatusTip => Dto.IsPrinted
        ? $"{Dto.PrintCount} badge{(Dto.PrintCount == 1 ? "" : "s")} printed, last on {Dto.LastPrintedUtc?.ToLocalTime():g}"
        : "No badge printed yet for this visitor ID";

    public string SourceText => Dto.SourceName ?? Dto.Source.ToString();
    public bool HasPhoto => _photos.GetFullPath(Dto.PhotoPath) is not null;

    /// <summary>Small photo for the list, decoded once at list size.</summary>
    public Bitmap? Thumbnail
    {
        get
        {
            if (_thumbnailLoaded) return _thumbnail;
            _thumbnailLoaded = true;
            var path = _photos.GetFullPath(Dto.PhotoPath);
            if (path is null) return null;
            try
            {
                using var stream = File.OpenRead(path);
                _thumbnail = Bitmap.DecodeToWidth(stream, 72);
            }
            catch (Exception ex)
            {
                InfoID.Shared.Logging.AppLog.For<VisitorRowViewModel>().Warn("Visitor thumbnail couldn't be loaded", ex);
            }
            return _thumbnail;
        }
    }

    public string Initials
    {
        get
        {
            var parts = FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length switch
            {
                0 => "?",
                1 => parts[0][..1].ToUpperInvariant(),
                _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant(),
            };
        }
    }
}
