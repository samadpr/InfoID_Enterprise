using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.CardEncoding.Contactless;
using InfoID.CardEncoding.Nfc;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Features.Rfid.Services;
using InfoID.Desktop.Features.Welcome.ViewModels;
using InfoID.Desktop.ViewModels.Base;
using InfoID.Domain.Common.Enums;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace InfoID.Desktop.Features.Rfid.ViewModels;

public sealed record CardDetailLine(string Label, string Value);

/// <summary>
/// File > Card Encoding (RFID / NFC): choose this PC's contactless reader and test cards.
/// Cards are detected automatically (the reader service's card events) and read on a
/// background thread: UID, card type, and for NFC tags the model, memory, write protection
/// and current NFC content. Read-only -- this page never writes to a card.
/// </summary>
public sealed partial class CardEncodingSettingsViewModel : ViewModelBase
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<CardEncodingSettingsViewModel>();

    private readonly INavigationService _navigation;
    private readonly IContactlessReaderService _readers;
    private readonly RfidSettingsStore _settings;
    private bool _loadingReaders;
    private bool _subscribed;
    private int _readVersion;

    public CardEncodingSettingsViewModel(INavigationService navigation, IContactlessReaderService readers, RfidSettingsStore settings)
    {
        _navigation = navigation;
        _readers = readers;
        _settings = settings;
    }

    public ObservableCollection<string> Readers { get; } = new();
    public ObservableCollection<CardDetailLine> CardDetails { get; } = new();
    public ObservableCollection<string> CardContent { get; } = new();

    [ObservableProperty] private string? _selectedReader;
    [ObservableProperty] private string _readerStatus = "Looking for card readers...";
    [ObservableProperty] private bool _isReaderReady;
    [ObservableProperty] private bool _noReadersFound;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasCard;
    [ObservableProperty] private string _cardStatus = "Place a card on the reader.";
    [ObservableProperty] private string? _cardNote;
    [ObservableProperty] private string? _errorMessage;

    public override void OnNavigatedTo(object? parameter)
    {
        // Subscribed only while the page is shown: the reader service is a singleton.
        if (!_subscribed)
        {
            _readers.CardInserted += OnCardInserted;
            _readers.CardRemoved += OnCardRemoved;
            _readers.ReaderError += OnReaderError;
            _subscribed = true;
        }
        _ = RefreshReadersAsync();
    }

    public override void OnNavigatedFrom()
    {
        if (_subscribed)
        {
            _readers.CardInserted -= OnCardInserted;
            _readers.CardRemoved -= OnCardRemoved;
            _readers.ReaderError -= OnReaderError;
            _subscribed = false;
        }
        _readVersion++; // ignore a read still running
    }

    [RelayCommand]
    private void GoHome() => _navigation.NavigateToRoot<WelcomeViewModel>();

    [RelayCommand]
    private async Task RefreshReadersAsync()
    {
        ErrorMessage = null;
        ReaderStatus = "Looking for card readers...";
        var names = await Task.Run(() => _readers.GetReaders());

        _loadingReaders = true;
        try
        {
            Readers.Clear();
            foreach (var name in names) Readers.Add(name);

            var saved = _settings.Current.ReaderName;
            SelectedReader = names.FirstOrDefault(n => n == _readers.SelectedReader)
                             ?? names.FirstOrDefault(n => n == saved)
                             ?? names.FirstOrDefault(n => n.Contains("OMNIKEY", StringComparison.OrdinalIgnoreCase)
                                                          && n.Contains(" CL", StringComparison.OrdinalIgnoreCase))
                             ?? names.FirstOrDefault();
        }
        finally
        {
            _loadingReaders = false;
        }

        NoReadersFound = names.Count == 0;
        await ApplyReaderAsync(SelectedReader);
    }

    partial void OnSelectedReaderChanged(string? value)
    {
        if (_loadingReaders) return;
        _ = ApplyReaderAsync(value);
    }

    private async Task ApplyReaderAsync(string? readerName)
    {
        _readVersion++;
        IsBusy = false;
        ClearCard("Place a card on the reader.");

        if (readerName is null)
        {
            IsReaderReady = false;
            ReaderStatus = "No card reader found. Connect the reader and click Refresh.";
            return;
        }

        try
        {
            if (_readers.SelectedReader != readerName)
                await Task.Run(() => _readers.SelectReader(readerName));
            _settings.Update(s => s.ReaderName = readerName);

            IsReaderReady = true;
            ReaderStatus = "Ready. Place a card on the reader to test it.";
            await ReadCardAsync(); // a card may already be lying on the reader
        }
        catch (ContactlessException ex)
        {
            IsReaderReady = false;
            ReaderStatus = "This reader isn't available.";
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>Reads the card now on the reader (also runs automatically when a card is placed).</summary>
    [RelayCommand]
    private async Task ReadCardAsync()
    {
        if (!IsReaderReady) return;

        var version = ++_readVersion;
        IsBusy = true;
        ErrorMessage = null;
        CardStatus = "Reading the card...";
        try
        {
            var result = await Task.Run(() => ReadCard());
            if (version != _readVersion) return;
            ShowCard(result);
        }
        catch (ContactlessException ex) when (ex.Error == ContactlessError.NoCard)
        {
            if (version == _readVersion) ClearCard("Place a card on the reader.");
        }
        catch (ContactlessException ex)
        {
            if (version != _readVersion) return;
            ClearCard("Couldn't read the card.");
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            Log.Warn("Card test read failed", ex);
            if (version != _readVersion) return;
            ClearCard("Couldn't read the card.");
            ErrorMessage = "Unexpected error while reading the card. See the log for details.";
        }
        finally
        {
            if (version == _readVersion) IsBusy = false;
        }
    }

    private sealed record CardReadResult(
        ContactlessCard Card, string? TagModel, int MemoryBytes, bool Formatted, bool ReadOnly,
        IReadOnlyList<NdefRecord> Records, string? Note);

    /// <summary>Background thread: connect (shared, read-only use), read UID/type and NFC content.</summary>
    /// <summary>Background thread: connect (shared, read-only use), read UID/type and NFC content.</summary>
    private CardReadResult ReadCard()
    {
        using var connection = _readers.Connect();
        var card = connection.Card;

        if (!Type2Tag.TryOpen(connection, out var tag) || tag is null)
            return new CardReadResult(card, null, 0, false, false, Array.Empty<NdefRecord>(),
                card.Type == ContactlessCardType.Unknown
                    ? "InfoID couldn't recognise this card as an NFC tag (NTAG / MIFARE Ultralight). Its UID can be used; writing data to it isn't supported yet."
                    : "This isn't an NFC tag (NTAG / MIFARE Ultralight). Its UID can be used; writing data to this card type isn't supported yet.");

        var records = tag.IsNdefFormatted ? tag.ReadNdef() : Array.Empty<NdefRecord>();
        var note = !tag.IsNdefFormatted ? "This tag isn't formatted for NFC data, so InfoID can't write to it."
                 : tag.IsReadOnly ? "This tag is locked (read-only): it can be read but not written."
                 : null;
        return new CardReadResult(card, tag.ModelName, tag.UserMemoryBytes, tag.IsNdefFormatted, tag.IsReadOnly, records, note);
    }


    private void ShowCard(CardReadResult r)
    {
        HasCard = true;
        CardStatus = "Card detected.";
        CardNote = r.Note;

        CardDetails.Clear();
        CardDetails.Add(new CardDetailLine("UID", FormatHex(r.Card.Uid)));
        CardDetails.Add(new CardDetailLine("Card type",
        r.TagModel is not null && r.Card.Type == ContactlessCardType.Unknown
        ? "NFC tag (detected by reading the card)"
        : Describe(r.Card.Type)));
        if (r.TagModel is not null)
        {
            CardDetails.Add(new CardDetailLine("Tag model", r.TagModel));
            CardDetails.Add(new CardDetailLine("Data memory", $"{r.MemoryBytes} bytes"));
            CardDetails.Add(new CardDetailLine("NFC formatted", r.Formatted ? "Yes" : "No"));
            CardDetails.Add(new CardDetailLine("Writable", r.ReadOnly ? "No (locked)" : "Yes"));
        }
        CardDetails.Add(new CardDetailLine("ATR", FormatHex(r.Card.Atr)));

        CardContent.Clear();
        foreach (var record in r.Records)
            CardContent.Add(record.Kind == NdefRecordKind.Uri ? $"Link: {record.Value}" : $"Text: {record.Value}");
        if (r.TagModel is not null && r.Records.Count == 0)
            CardContent.Add("(no NFC data on this card)");
    }

    private void ClearCard(string status)
    {
        HasCard = false;
        CardStatus = status;
        CardNote = null;
        CardDetails.Clear();
        CardContent.Clear();
    }

    // Reader events arrive on a background thread.
    private void OnCardInserted(object? sender, CardPresenceEventArgs e) =>
        Dispatcher.UIThread.Post(() => _ = ReadCardAsync());

    private void OnCardRemoved(object? sender, CardPresenceEventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            _readVersion++;
            IsBusy = false;
            ClearCard("Card removed. Place a card on the reader.");
        });

    private void OnReaderError(object? sender, ContactlessException e) =>
        Dispatcher.UIThread.Post(() =>
        {
            _readVersion++;
            IsBusy = false;
            IsReaderReady = false;
            ClearCard("Place a card on the reader.");
            ReaderStatus = "The reader stopped responding. Reconnect it and click Refresh.";
            ErrorMessage = e.Message;
        });

    private static string FormatHex(byte[] bytes) =>
        bytes.Length == 0 ? "-" : string.Join(" ", bytes.Select(b => b.ToString("X2")));

    private static string Describe(ContactlessCardType type) => type switch
    {
        ContactlessCardType.MifareUltralightOrNtag => "NFC tag (MIFARE Ultralight / NTAG)",
        ContactlessCardType.MifareUltralightC => "MIFARE Ultralight C",
        ContactlessCardType.MifareClassic1K => "MIFARE Classic 1K",
        ContactlessCardType.MifareClassic4K => "MIFARE Classic 4K",
        ContactlessCardType.MifareMini => "MIFARE Mini",
        _ => "Unknown / other",
    };
}
