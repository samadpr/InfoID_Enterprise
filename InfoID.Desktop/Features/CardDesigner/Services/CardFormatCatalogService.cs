using InfoID.Desktop.Features.BlankCard.Models;
using InfoID.Desktop.Features.CardDesigner.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static InfoID.Desktop.Features.CardDesigner.Models.CardFormatDefinition;

namespace InfoID.Desktop.Features.CardDesigner.Services;

public sealed class CardFormatCatalogService : ICardFormatCatalogService
{
    private readonly ICustomCardModelStore _customStore;

    // Verified dimensions (see chat for sources): CR80 uses the exact SRS/ISO 7810 ID-1
    // figure; CR79/90/100 use commonly-cited industry figures (precise mm conversions of
    // the widely-quoted inch values, since these three don't have one universal ISO spec).
    private static readonly IReadOnlyList<CardFormatDefinition> StandardFormats = new List<CardFormatDefinition>
    {
        new()
        {
            Id = "cr79",
            FormatCode = "CR79",
            Name = "CR79",
            Description = "Slightly smaller than CR80 -- fits inside the well of a proximity card.",
            WidthMm = 83.90,
            HeightMm = 52.10,
            Category = CardFormatCategory.Standard,
            DefaultOrientation = CardOrientation.Landscape,
            CornerRadiusMm = 3.18,
        },
        new()
        {
            Id = "cr80",
            FormatCode = "CR80",
            Name = "CR80",
            Description = "Standard credit-card-sized ID card (ISO/IEC 7810 ID-1).",
            WidthMm = 85.60,
            HeightMm = 53.98,
            Category = CardFormatCategory.Standard,
            DefaultOrientation = CardOrientation.Landscape,
            CornerRadiusMm = 3.18,
        },
        new()
        {
            Id = "cr90",
            FormatCode = "CR90",
            Name = "CR90",
            Description = "Driver's-license size, slightly larger than CR80.",
            WidthMm = 92.20,
            HeightMm = 60.20,
            Category = CardFormatCategory.Standard,
            DefaultOrientation = CardOrientation.Landscape,
            CornerRadiusMm = 3.18,
        },
        new()
        {
            Id = "cr100",
            FormatCode = "CR100",
            Name = "CR100",
            Description = "Oversized / \"military\" card, roughly 42% larger than CR80.",
            WidthMm = 98.55,
            HeightMm = 66.80,
            Category = CardFormatCategory.Standard,
            DefaultOrientation = CardOrientation.Landscape,
            CornerRadiusMm = 3.18,
        },
    };

    public CardFormatCatalogService(ICustomCardModelStore customStore)
    {
        _customStore = customStore;
    }

    public IReadOnlyList<CardFormatDefinition> GetStandardFormats() => StandardFormats;

    public async Task<IReadOnlyList<CardFormatDefinition>> GetCustomFormatsAsync()
    {
        var models = await _customStore.GetAllAsync();
        return models.Select(ToDefinition).ToList();
    }

    public async Task<IReadOnlyList<CardFormatDefinition>> GetAllAsync()
    {
        var custom = await GetCustomFormatsAsync();
        return StandardFormats.Concat(custom).ToList();
    }

    public async Task<CardFormatDefinition> SaveCustomFormatAsync(
        string name, double widthMm, double heightMm, double cornerRadiusMm, CardOrientation orientation)
    {
        var model = new Models.CustomCardModel
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            WidthMm = widthMm,
            HeightMm = heightMm,
            CornerRadiusMm = cornerRadiusMm,
            Orientation = orientation,
        };

        await _customStore.SaveAsync(model);
        return ToDefinition(model);
    }

    public Task DeleteCustomFormatAsync(string id) => _customStore.DeleteAsync(id);

    private static CardFormatDefinition ToDefinition(Models.CustomCardModel model) => new()
    {
        Id = model.Id,
        FormatCode = model.Name,
        Name = model.Name,
        Description = "Custom model",
        WidthMm = model.WidthMm,
        HeightMm = model.HeightMm,
        Category = CardFormatCategory.Custom,
        DefaultOrientation = model.Orientation,
        CornerRadiusMm = model.CornerRadiusMm,
    };
}