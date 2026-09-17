namespace InfoID.Desktop.Features.Printing.Models;

/// <summary>How the rasterized card is sent to the printer. Composite is a normal color
/// pass; MonoChrome converts every pixel to black/white first (a real grayscale +
/// threshold transform, not a display filter); CompositeAndMonoChrome emits both --
/// a real ID-card printer with a separate resin/K-panel ribbon takes a full-color pass
/// plus a black-only pass, and this reproduces that as two real rendered pages per
/// card side rather than one page pretending to be both.</summary>
public enum PrintColorMode { MonoChrome, Composite, CompositeAndMonoChrome }

/// <summary>Matches the reference's Antialiasing field. Only the Yes/No ends of this are
/// currently backed by a real, distinct rendering difference (Avalonia's
/// RenderOptions.EdgeMode, toggled in CardRasterizer) -- "Only Text" and "Only Images"
/// aren't separately controllable at the DrawingContext level this renderer uses, so
/// they currently behave the same as "Yes" (antialiased). Kept in the option list to
/// match the reference UI, not hidden, but this is a real, disclosed limitation rather
/// than four modes silently doing the same thing without saying so.</summary>
public enum PrintAntialiasMode { Yes, OnlyText, OnlyImages, No }

/// <summary>Auto-saves a copy of what was printed as part of the Print command itself
/// (distinct from the dialog's manual "Digital Copy" button, which always asks where to
/// save one on demand) -- Off does nothing extra, PdfFile writes one multi-page PDF per
/// print run, ImageFiles writes one PNG per page. Saved under the app's own
/// DigitalCopies folder, timestamped per run.</summary>
public enum DigitalCopyMode { Off, PdfFile, ImageFiles }

/// <summary>What an Advanced Print Operations status-mapping row writes back after a
/// successful print -- PrintStatus (Printed/NotPrinted) is always tracked regardless;
/// PrintCounter and PrintDate are the two additional, real effects a mapping row can opt
/// a record into (a running per-record print count, and a last-printed timestamp
/// distinct from the always-on one used for sorting/display).</summary>
public enum PrintStatusMarker { PrintStatus, PrintCounter, PrintDate, PrintStatusAndCounter, PrintStatusAndCounterAndDate }
