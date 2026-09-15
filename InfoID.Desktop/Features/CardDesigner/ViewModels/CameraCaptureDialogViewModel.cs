using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Features.CardDesigner.Models;
using InfoID.Desktop.Features.CardDesigner.Services;

namespace InfoID.Desktop.Features.CardDesigner.ViewModels;

/// <summary>
/// Priority 12 (Take Photo / Use Camera) + SRS FR-PHS-1 ("Capture photos via connected
/// webcam with live preview, countdown timer and retake"). Owns device selection, the
/// live preview stream, the capture countdown, and retake -- everything except the
/// actual camera hardware access, which is delegated entirely to ICameraService (see
/// that interface and FlashCapCameraService's own doc comments for this feature's
/// honest confidence level: this ViewModel's own logic follows patterns already proven
/// throughout this codebase, but it depends on a camera library integration that could
/// not be tested against real hardware from the environment this was written in).
///
/// State machine: Loading -> (NoCameraAvailable | Live) -> [Capture clicked] ->
/// Countdown -> Captured -> ([Retake] -> Live again | [Done] -> closes with the
/// captured frame). Cancel/BrowseInstead can be chosen from Loading, NoCameraAvailable
/// or Live.
/// </summary>
public sealed partial class CameraCaptureDialogViewModel : DialogViewModelBase<CameraCaptureResult>
{
    /// <summary>Shows "InfoID Take Photo" in the hosted window's title bar, matching
    /// the same per-dialog title treatment ImageEditorDialogViewModel uses.</summary>
    public override string Title => "InfoID Take Photo";

    private readonly ICameraService _cameraService;
    private ICameraSession? _session;
    private Bitmap? _lastLiveFrame;
    private bool _capturing;

    public CameraCaptureDialogViewModel(ICameraService cameraService)
    {
        _cameraService = cameraService;
        _ = LoadDevicesAsync();
    }

    // ------------------------------------------------------------------- state ----

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _hasNoCameraAvailable;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<CameraDeviceInfo> Devices { get; } = new();

    [ObservableProperty]
    private CameraDeviceInfo? _selectedDevice;

    [ObservableProperty]
    private IImage? _previewImage;

    /// <summary>True once a frame has been frozen for review (post-countdown) -- the
    /// View swaps the "Capture" button for "Retake"/"Use This Photo" while this is
    /// true, and PreviewImage stops updating from live frames until Retake.</summary>
    [ObservableProperty]
    private bool _isCaptured;

    /// <summary>"3", "2", "1" while counting down, null otherwise -- the View shows
    /// this as a large overlay on top of the live preview.</summary>
    [ObservableProperty]
    private string? _countdownText;

    public bool IsLive => !IsLoading && !HasNoCameraAvailable && !IsCaptured && CountdownText is null;

    /// <summary>Whether the whole live/countdown/captured section (as opposed to the
    /// Loading or No Camera Found messages) should be shown at all -- separate from
    /// IsLive, which additionally excludes the countdown/captured sub-states.</summary>
    public bool ShowCameraUi => !IsLoading && !HasNoCameraAvailable;

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(ShowCameraUi));
    }

    partial void OnHasNoCameraAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(ShowCameraUi));
    }

    partial void OnIsCapturedChanged(bool value) => OnPropertyChanged(nameof(IsLive));
    partial void OnCountdownTextChanged(string? value) => OnPropertyChanged(nameof(IsLive));

    // --------------------------------------------------------------- device setup ----

    private async Task LoadDevicesAsync()
    {
        IsLoading = true;
        try
        {
            var devices = await _cameraService.GetAvailableDevicesAsync();
            Devices.Clear();
            foreach (var d in devices) Devices.Add(d);

            if (Devices.Count == 0)
            {
                HasNoCameraAvailable = true;
                return;
            }

            // Setting SelectedDevice below triggers OnSelectedDeviceChanged, which
            // itself opens the device -- deliberately not also calling
            // OpenSelectedDeviceAsync() explicitly here too, which would otherwise
            // race two concurrent OpenAsync calls against the same device (most
            // webcams only tolerate one open handle, and the first session's handle
            // would leak since only the second gets assigned to _session).
            SelectedDevice = Devices[0];
        }
        catch (Exception)
        {
            // Enumeration itself should already fail gracefully inside
            // ICameraService, but a defensive catch here too means a surprise
            // exception anywhere in this path still reads as "no camera" instead of
            // an unhandled crash of the whole designer.
            HasNoCameraAvailable = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedDeviceChanged(CameraDeviceInfo? value)
    {
        if (value is not null) _ = OpenSelectedDeviceAsync();
    }

    private async Task OpenSelectedDeviceAsync()
    {
        if (SelectedDevice is not { } device) return;

        await CloseSessionAsync();
        ErrorMessage = null;

        try
        {
            _session = await _cameraService.OpenAsync(device);
            _session.FrameReceived += OnFrameReceived;
        }
        catch (Exception ex)
        {
            // A specific device failing to open (disconnected, in use elsewhere,
            // permission denied) is NOT the same as "no camera available" -- the
            // device list may still have other entries worth trying, so this shows
            // an inline error rather than falling all the way back to
            // HasNoCameraAvailable's "no camera at all" messaging.
            ErrorMessage = $"Couldn't open '{device.Name}': {ex.Message}";
        }
    }

    private void OnFrameReceived(object? sender, Bitmap frame)
    {
        _lastLiveFrame = frame;
        if (_capturing || IsCaptured) return; // frozen on the captured frame -- ignore further live frames

        Dispatcher.UIThread.Post(() => PreviewImage = frame);
    }

    // ------------------------------------------------------------------ commands ----

    [RelayCommand]
    private async Task Capture()
    {
        if (_capturing || IsCaptured) return;
        _capturing = true;
        try
        {
            foreach (var n in new[] { "3", "2", "1" })
            {
                CountdownText = n;
                await Task.Delay(1000);
            }
            CountdownText = null;

            if (_lastLiveFrame is { } frame)
            {
                PreviewImage = frame;
                IsCaptured = true;
            }
            else
            {
                ErrorMessage = "No frame was available to capture -- the live preview may not have started yet. Try again in a moment.";
            }
        }
        finally
        {
            _capturing = false;
        }
    }

    [RelayCommand]
    private void Retake()
    {
        IsCaptured = false;
        // The next frame ICameraSession delivers (the session is still open and
        // running throughout capture/review) resumes live preview automatically via
        // OnFrameReceived -- no need to reopen the device.
    }

    [RelayCommand]
    private async Task Done()
    {
        if (PreviewImage is not Bitmap captured) return;
        await CloseSessionAsync();
        RequestClose(new CameraCaptureResult { CapturedBitmap = captured });
    }

    [RelayCommand]
    private async Task Cancel()
    {
        await CloseSessionAsync();
        RequestClose(null);
    }

    [RelayCommand]
    private async Task BrowseInstead()
    {
        await CloseSessionAsync();
        RequestClose(new CameraCaptureResult { FallThroughToBrowse = true });
    }

    private async Task CloseSessionAsync()
    {
        if (_session is null) return;
        var session = _session;
        _session = null;
        session.FrameReceived -= OnFrameReceived;
        try
        {
            await session.DisposeAsync();
        }
        catch (Exception)
        {
            // Best-effort cleanup -- the device may already be gone.
        }
    }
}
