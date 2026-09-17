using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace InfoID.Desktop.Features.Printing.Services;

/// <summary>
/// Hand-written, dependency-free PDF generation for the "Digital Copy" feature (save a
/// real, openable record of what was printed) -- no PDF library was added for this
/// because the actual need is narrow and simple: one full-bleed raster image per page,
/// nothing else (no text, fonts, or vector graphics inside the PDF itself, since the
/// card design is already rendered to a bitmap by CardRasterizer before it gets here).
/// That's a small, well-defined subset of the PDF spec -- an image XObject per page, a
/// content stream that draws it filling the page, and a standard object/xref/trailer
/// structure -- well within what's safe to hand-roll, unlike barcode/QR symbol encoding
/// (see BarcodeRenderer's own doc comment on why THAT was never hand-rolled).
///
/// Images are embedded as raw RGB pixels compressed with System.IO.Compression.ZLibStream
/// (.NET's built-in zlib implementation, zero extra dependency) under PDF's own
/// /FlateDecode filter -- PDF's FlateDecode is literally zlib-wrapped deflate, so this
/// needs no format conversion beyond stripping the alpha channel.
/// </summary>
public static class CardPdfWriter
{
    public readonly record struct PdfPage(byte[] RgbaPixels, int PixelWidth, int PixelHeight, double PageWidthMm, double PageHeightMm);

    private const double PointsPerMm = 72.0 / 25.4;

    public static byte[] Build(IReadOnlyList<PdfPage> pages)
    {
        if (pages.Count == 0) throw new ArgumentException("At least one page is required.", nameof(pages));

        var objects = new List<byte[]>(); // index 0 == object number 1
        var pageObjectNumbers = new List<int>();

        // Object 1: Catalog, Object 2: Pages tree -- reserved, written after we know the
        // full kids list.
        objects.Add(Array.Empty<byte>()); // placeholder for Catalog (obj 1)
        objects.Add(Array.Empty<byte>()); // placeholder for Pages tree (obj 2)

        foreach (var page in pages)
        {
            var rgb = ToRgb(page.RgbaPixels, page.PixelWidth, page.PixelHeight);
            var compressed = Deflate(rgb);

            var widthPt = page.PageWidthMm * PointsPerMm;
            var heightPt = page.PageHeightMm * PointsPerMm;

            var imageObjNum = objects.Count + 1;
            objects.Add(BuildImageObject(imageObjNum, page.PixelWidth, page.PixelHeight, compressed));

            var contentObjNum = objects.Count + 1;
            var contentStream = Encoding.ASCII.GetBytes(
                $"q {F(widthPt)} 0 0 {F(heightPt)} 0 0 cm /Im0 Do Q");
            objects.Add(BuildStreamObject(contentObjNum, contentStream, extraDict: null));

            var pageObjNum = objects.Count + 1;
            pageObjectNumbers.Add(pageObjNum);
            objects.Add(BuildPageObject(pageObjNum, parentObjNum: 2, widthPt, heightPt, contentObjNum, imageObjNum));
        }

        var kids = string.Join(' ', pageObjectNumbers.ConvertAll(n => $"{n} 0 R"));
        objects[0] = BuildRawObject(1, $"<< /Type /Catalog /Pages 2 0 R >>");
        objects[1] = BuildRawObject(2, $"<< /Type /Pages /Kids [{kids}] /Count {pageObjectNumbers.Count} >>");

        return Assemble(objects);
    }

    private static byte[] Assemble(List<byte[]> objects)
    {
        using var output = new MemoryStream();
        void Write(string s) => output.Write(Encoding.ASCII.GetBytes(s));
        void WriteBytes(byte[] b) => output.Write(b);

        Write("%PDF-1.4\n");

        var offsets = new long[objects.Count + 1]; // 1-indexed
        for (var i = 0; i < objects.Count; i++)
        {
            offsets[i + 1] = output.Position;
            WriteBytes(objects[i]);
        }

        var xrefStart = output.Position;
        Write($"xref\n0 {objects.Count + 1}\n");
        Write("0000000000 65535 f \n");
        for (var i = 1; i <= objects.Count; i++)
        {
            Write($"{offsets[i]:D10} 00000 n \n");
        }

        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefStart}\n%%EOF");

        return output.ToArray();
    }

    private static byte[] BuildRawObject(int objNum, string dict)
    {
        var text = $"{objNum} 0 obj\n{dict}\nendobj\n";
        return Encoding.ASCII.GetBytes(text);
    }

    private static byte[] BuildImageObject(int objNum, int width, int height, byte[] flateCompressedRgb)
    {
        var header = Encoding.ASCII.GetBytes(
            $"{objNum} 0 obj\n<< /Type /XObject /Subtype /Image /Width {width} /Height {height} " +
            $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length {flateCompressedRgb.Length} >>\nstream\n");
        var footer = Encoding.ASCII.GetBytes("\nendstream\nendobj\n");

        var result = new byte[header.Length + flateCompressedRgb.Length + footer.Length];
        Buffer.BlockCopy(header, 0, result, 0, header.Length);
        Buffer.BlockCopy(flateCompressedRgb, 0, result, header.Length, flateCompressedRgb.Length);
        Buffer.BlockCopy(footer, 0, result, header.Length + flateCompressedRgb.Length, footer.Length);
        return result;
    }

    private static byte[] BuildStreamObject(int objNum, byte[] content, string? extraDict)
    {
        var header = Encoding.ASCII.GetBytes(
            $"{objNum} 0 obj\n<< /Length {content.Length}{extraDict} >>\nstream\n");
        var footer = Encoding.ASCII.GetBytes("\nendstream\nendobj\n");

        var result = new byte[header.Length + content.Length + footer.Length];
        Buffer.BlockCopy(header, 0, result, 0, header.Length);
        Buffer.BlockCopy(content, 0, result, header.Length, content.Length);
        Buffer.BlockCopy(footer, 0, result, header.Length + content.Length, footer.Length);
        return result;
    }

    private static byte[] BuildPageObject(int objNum, int parentObjNum, double widthPt, double heightPt, int contentObjNum, int imageObjNum) =>
        BuildRawObject(objNum,
            $"<< /Type /Page /Parent {parentObjNum} 0 R /MediaBox [0 0 {F(widthPt)} {F(heightPt)}] " +
            $"/Resources << /XObject << /Im0 {imageObjNum} 0 R >> >> /Contents {contentObjNum} 0 R >>");

    /// <summary>Drops the alpha channel and reorders BGRA/RGBA (whatever the source
    /// bitmap's native pixel format is) into plain top-to-bottom RGB triples -- PDF
    /// images have no row-stride padding concept, so this also removes any source row
    /// padding beyond width*4 bytes.</summary>
    private static byte[] ToRgb(byte[] bgraOrRgba, int width, int height)
    {
        var sourceStride = bgraOrRgba.Length / height;
        var rgb = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            var srcRow = y * sourceStride;
            var dstRow = y * width * 3;
            for (var x = 0; x < width; x++)
            {
                var srcIndex = srcRow + x * 4;
                var dstIndex = dstRow + x * 3;
                // Source is BGRA (Avalonia's native Bgra8888 pixel format -- see
                // ImageEditingService.ProcessPixels's own confirmed byte order).
                rgb[dstIndex] = bgraOrRgba[srcIndex + 2];     // R
                rgb[dstIndex + 1] = bgraOrRgba[srcIndex + 1]; // G
                rgb[dstIndex + 2] = bgraOrRgba[srcIndex];     // B
            }
        }
        return rgb;
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
