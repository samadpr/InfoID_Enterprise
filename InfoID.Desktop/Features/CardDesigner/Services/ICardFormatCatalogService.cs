using System.Collections.Generic;
using System.Threading.Tasks;
using InfoID.Desktop.Features.CardDesigner.Models;

namespace InfoID.Desktop.Features.CardDesigner.Services;

public interface ICardFormatCatalogService
{
    /// <summary>CR79 / CR80 / CR90 / CR100 -- fixed, cannot be deleted.</summary>
    IReadOnlyList<CardFormatDefinition> GetStandardFormats();

    /// <summary>User's saved "My Models" custom formats.</summary>
    Task<IReadOnlyList<CardFormatDefinition>> GetCustomFormatsAsync();

    Task<IReadOnlyList<CardFormatDefinition>> GetAllAsync();

    Task<CardFormatDefinition> SaveCustomFormatAsync(string name, double widthMm, double heightMm, double cornerRadiusMm, Features.BlankCard.Models.CardOrientation orientation);

    Task DeleteCustomFormatAsync(string id);
}