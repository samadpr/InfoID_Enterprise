using System.Collections.Generic;
using System.Threading.Tasks;
using InfoID.Desktop.Features.BlankCard.Models;

namespace InfoID.Desktop.Features.BlankCard.Services; //of blank-card formats and their filter tags. The Blank Card page
/// binds to this abstraction only, so the sample catalog can later be replaced with a
/// database/plugin-backed catalog (see FR-MOD-3 plugin architecture) without UI changes.
/// </summary>
public interface IBlankCardCatalogService
{
    Task<IReadOnlyList<string>> GetFilterTagsAsync();

    Task<IReadOnlyList<CardFormatOption>> GetFormatsAsync();
}
