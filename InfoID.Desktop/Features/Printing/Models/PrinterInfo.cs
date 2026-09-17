namespace InfoID.Desktop.Features.Printing.Models;

/// <summary>One printer as reported by the OS -- real installed printers only, never
/// fabricated. IsOnline reflects what the OS itself reports (a printer can be installed
/// but offline/out of paper/etc.); a platform that can't determine this reports true so
/// the UI doesn't show every printer as falsely offline.</summary>
public sealed record PrinterInfo(string Name, bool IsDefault, bool IsOnline);
