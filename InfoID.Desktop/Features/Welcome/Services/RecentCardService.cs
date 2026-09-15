using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using InfoID.Desktop.Features.CardDesigner.Services;
using InfoID.Desktop.Features.Welcome.Models;

namespace InfoID.Desktop.Features.Welcome.Services;

/// <summary>
/// Merges two sources: database-backed designs (ICardDesignRepository, the same
/// Template rows the Card Designer normally saves to) and file-backed designs opened
/// via File > Import (IRecentFilesService) -- those have no Template row at all, so
/// they'd otherwise never appear in Recent Cards. The Welcome page never touches either
/// source directly; it only knows IRecentCardService, so nothing changed on the
/// ViewModel/View side to add file-backed support.
/// </summary>
public sealed class RecentCardService : IRecentCardService
{
    private readonly ICardDesignRepository _cardDesignRepository;
    private readonly IThumbnailService _thumbnailService;
    private readonly IRecentFilesService _recentFilesService;
    private readonly IInfoIdFileService _infoIdFileService;

    public RecentCardService(
        ICardDesignRepository cardDesignRepository, IThumbnailService thumbnailService,
        IRecentFilesService recentFilesService, IInfoIdFileService infoIdFileService)
    {
        _cardDesignRepository = cardDesignRepository;
        _thumbnailService = thumbnailService;
        _recentFilesService = recentFilesService;
        _infoIdFileService = infoIdFileService;
    }

    public async Task<IReadOnlyList<RecentCardItem>> GetRecentCardsAsync(int maxCount = 50)
    {
        var recent = await _cardDesignRepository.GetRecentAsync(maxCount);
        var dbItems = recent.Select(r => new RecentCardItem
        {
            Id = r.TemplateId.ToString(),
            Name = r.Name,
            Subtitle = $"{r.CardFormatLabel} \u2022 {FormatRelativeDate(r.LastOpenedDate ?? r.ModifiedDate)}",
            LastModified = r.ModifiedDate,
            IsPinned = r.IsPinned,
            ThumbnailImage = LoadThumbnail(r.TemplateId),
        });

        var fileItems = new List<RecentCardItem>();
        foreach (var entry in _recentFilesService.GetRecentFiles(maxCount))
        {
            // A file the user moved/deleted since it was last opened just silently
            // drops out of the list -- better than showing a tile that can't be opened.
            if (!File.Exists(entry.FilePath)) continue;

            fileItems.Add(new RecentCardItem
            {
                Id = "file:" + entry.FilePath,
                Name = entry.Name,
                Subtitle = $"Local file \u2022 {FormatRelativeDate(entry.LastOpenedUtc)}",
                LastModified = entry.LastOpenedUtc,
                IsPinned = entry.Pinned,
                ThumbnailImage = await LoadFileThumbnailAsync(entry.FilePath),
            });
        }

        return dbItems.Concat(fileItems)
            .OrderByDescending(i => i.IsPinned)
            .ThenByDescending(i => i.LastModified)
            .Take(maxCount)
            .ToList();
    }

    public Task DeleteAsync(RecentCardItem item)
    {
        if (TryGetFilePath(item, out var filePath)) return _recentFilesService.RemoveAsync(filePath);
        if (long.TryParse(item.Id, out var templateId)) return _cardDesignRepository.DeleteAsync(templateId);
        return Task.CompletedTask;
    }

    public Task TogglePinAsync(RecentCardItem item)
    {
        if (TryGetFilePath(item, out var filePath)) return _recentFilesService.SetPinnedAsync(filePath, !item.IsPinned);
        if (long.TryParse(item.Id, out var templateId)) return _cardDesignRepository.SetPinnedAsync(templateId, !item.IsPinned);
        return Task.CompletedTask;
    }

    /// <summary>Decodes the "file:" prefix scheme used throughout this service (and by
    /// WelcomeViewModel.OpenRecent) to distinguish a file-backed RecentCardItem from a
    /// database-backed one sharing the same Id property.</summary>
    private static bool TryGetFilePath(RecentCardItem item, out string filePath)
    {
        if (item.Id.StartsWith("file:", StringComparison.Ordinal))
        {
            filePath = item.Id["file:".Length..];
            return true;
        }

        filePath = string.Empty;
        return false;
    }

    /// <summary>Loaded here, once, rather than via a XAML value converter -- keeps the
    /// View a plain {Binding ThumbnailImage} with no converter/type-resolution surface
    /// at all. A missing or corrupt thumbnail file (e.g. deleted by the user, or a design
    /// saved before this feature existed) must never break the whole Recent Cards list;
    /// it just falls back to null here.</summary>
    private IImage? LoadThumbnail(long templateId)
    {
        var path = _thumbnailService.GetThumbnailPath(templateId);
        if (path is null) return null;

        try
        {
            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>File-backed designs have no Template.Id to cache a thumbnail file
    /// under, so this re-imports the file and renders it fresh each time the Recent
    /// Cards list loads. A corrupt/unreadable file just gets no thumbnail (falls back to
    /// the tile's plain background) rather than breaking the whole list.</summary>
    private async Task<IImage?> LoadFileThumbnailAsync(string filePath)
    {
        try
        {
            var document = await _infoIdFileService.ImportAsync(filePath);
            return await _thumbnailService.RenderInMemoryAsync(document);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatRelativeDate(DateTime date)
    {
        var span = DateTime.UtcNow - date;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
        return date.ToLocalTime().ToString("MMM d, yyyy");
    }
}