using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InfoID.Desktop.Features.CardDesigner.Models.Document;

namespace InfoID.Desktop.Features.CardDesigner.Services;

public sealed class InfoIdFileService : IInfoIdFileService
{
    private const string FormatMarker = "InfoID.CardDesign";
    private const int CurrentFormatVersion = 1;

    private sealed class Envelope
    {
        public string Format { get; set; } = FormatMarker;
        public int FormatVersion { get; set; } = CurrentFormatVersion;
        public CardDesignDocument? Document { get; set; }
    }

    public async Task ExportAsync(CardDesignDocument document, string filePath, CancellationToken ct = default)
    {
        var envelope = new Envelope { Document = document };
        var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true });

        try
        {
            await File.WriteAllTextAsync(filePath, json, ct);
        }
        catch (Exception ex)
        {
            throw new InfoIdFileException($"Couldn't save the file: {ex.Message}", ex);
        }
    }

    public async Task<CardDesignDocument> ImportAsync(string filePath, CancellationToken ct = default)
    {
        string json;
        try
        {
            json = await File.ReadAllTextAsync(filePath, ct);
        }
        catch (Exception ex)
        {
            throw new InfoIdFileException($"Couldn't open the file: {ex.Message}", ex);
        }

        Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(json);
        }
        catch (Exception ex)
        {
            throw new InfoIdFileException("This file isn't a valid InfoID design (.infoid) file -- it doesn't contain readable JSON.", ex);
        }

        if (envelope is null || envelope.Format != FormatMarker || envelope.Document is null)
        {
            throw new InfoIdFileException("This file isn't a recognized InfoID design (.infoid) file.");
        }

        if (envelope.FormatVersion > CurrentFormatVersion)
        {
            throw new InfoIdFileException(
                $"This file was exported from a newer version of InfoID (format {envelope.FormatVersion}) that this version doesn't support yet.");
        }

        return envelope.Document;
    }
}
