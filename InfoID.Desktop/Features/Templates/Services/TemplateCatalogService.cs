using System.Collections.Generic;
using System.Threading.Tasks;
using InfoID.Desktop.Features.BlankCard.Models;
using InfoID.Desktop.Features.Templates.Models;

namespace InfoID.Desktop.Features.Templates.Services;

/// <summary>
/// Sample/in-memory template gallery matching the categories called out in the SRS
/// (FR-DES-10: school/corporate/event/hospital, extended to the full target-market
/// list in Section 3.2). Replace with an ITemplateService-backed implementation once
/// gallery templates are seeded in the database.
/// </summary>
public sealed class TemplateCatalogService : ITemplateCatalogService
{
    private static readonly IReadOnlyList<string> Categories = new[]
    {
        "Corporate", "Education", "Government & Institutions", "Health",
        "Hospitality & Leisure", "Retail", "Transportation", "User Templates",
    };

    private static readonly IReadOnlyList<TemplateCatalogItem> Templates = new List<TemplateCatalogItem>
    {
        New("corp-access-1", "Corporate Access Badge", "Corporate", "Access"),
        New("corp-access-2", "Executive ID Card", "Corporate", "Access"),
        New("corp-business-1", "Modern Business Card", "Corporate", "Business"),
        New("corp-business-2", "Minimal Employee Card", "Corporate", "Business"),
        New("edu-student-1", "University Student ID", "Education", "Access"),
        New("edu-student-2", "School Photo ID", "Education", "Access"),
        New("edu-staff-1", "Faculty ID Card", "Education", "Business"),
        New("gov-id-1", "Government Employee ID", "Government & Institutions", "Access"),
        New("gov-id-2", "Department Clearance Card", "Government & Institutions", "Access"),
        New("health-staff-1", "Hospital Staff Badge", "Health", "Access"),
        New("health-patient-1", "Patient Wristband Card", "Health", "Access"),
        New("hosp-member-1", "Hotel Membership Card", "Hospitality & Leisure", "Business"),
        New("hosp-event-1", "Conference Attendee Badge", "Hospitality & Leisure", "Access"),
        New("retail-loyalty-1", "Retail Loyalty Card", "Retail", "Business"),
        New("retail-staff-1", "Retail Staff ID", "Retail", "Access"),
        New("transport-pass-1", "Transit Pass Card", "Transportation", "Access"),
    };

    public Task<IReadOnlyList<string>> GetCategoriesAsync() => Task.FromResult(Categories);

    public Task<IReadOnlyList<TemplateCatalogItem>> GetTemplatesAsync() => Task.FromResult(Templates);

    private static TemplateCatalogItem New(string id, string name, string category, string format) =>
        new()
        {
            Id = id,
            Name = name,
            Category = category,
            Format = format,
            Orientation = CardOrientation.Landscape,
            AccentColorHex = PickAccent(category),
        };

    private static string PickAccent(string category) => category switch
    {
        "Corporate" => "#DA3025",
        "Education" => "#2E6F9E",
        "Government & Institutions" => "#4B5563",
        "Health" => "#2E8B57",
        "Hospitality & Leisure" => "#B8860B",
        "Retail" => "#7E3FF2",
        "Transportation" => "#0F766E",
        _ => "#DA3025",
    };
}
