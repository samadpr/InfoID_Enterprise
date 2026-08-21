using System.Collections.Generic;
using System.Threading.Tasks;
using InfoID.Desktop.Features.Templates.Models;

namespace InfoID.Desktop.Features.Templates.Services;

/// <summary>
/// Provides the browsable template gallery and its category list. The Templates page
/// depends only on this interface; a future implementation can back it with
/// InfoID.Application's ITemplateService (gallery templates + user templates from the
/// local database) without any UI changes.
/// </summary>
public interface ITemplateCatalogService
{
    Task<IReadOnlyList<string>> GetCategoriesAsync();

    Task<IReadOnlyList<TemplateCatalogItem>> GetTemplatesAsync();
}
