using System.Threading;
using System.Threading.Tasks;
using InfoID.Desktop.Features.CardDesigner.Models.Document;

namespace InfoID.Desktop.Features.CardDesigner.Services;

/// <summary>
/// Portable .infoid file format (Part 9/62: "export/import of template files (portable
/// .json/.xml template format)"). A thin JSON envelope around the same
/// CardDesignDocument serialization already used for database version snapshots and
/// tab duplication -- no new serialization logic, just a format marker so Import can
/// reject a random JSON file with a clear message instead of a cryptic deserialization
/// crash.
/// </summary>
public interface IInfoIdFileService
{
    Task ExportAsync(CardDesignDocument document, string filePath, CancellationToken ct = default);

    /// <summary>Throws InfoIdFileException with a user-safe message if the file can't
    /// be read, isn't valid JSON, or isn't a recognized InfoID design file.</summary>
    Task<CardDesignDocument> ImportAsync(string filePath, CancellationToken ct = default);
}
