using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace InfoID.Desktop.Features.CardDesigner.Views.Controls;

/// <summary>
/// Code-behind for ColorPickerField.axaml. Owns the HSVA to hex conversion and keeps
/// the SV box, hue/alpha sliders, RGB boxes, hex textbox and swatch previews in sync
/// without feedback loops. This is deliberately code-behind rather than a ViewModel:
/// it's presentation-only state for a single reusable low-level control (same reasoning
/// as CardCanvasView's own code-behind), and the one thing callers actually bind to is
/// the public ColorHex StyledProperty.
///
/// Priority 9 rework: the picker used to be three plain sliders (Hue/Saturation/
/// Brightness) plus a hex box. Saturation and Brightness are now a single drag-anywhere
/// 2D box (the standard "SV square" every mainstream color picker uses), which is far
/// more intuitive for someone who doesn't think in HSV terms, and RGB numeric inputs
/// were added alongside hex for people who think in 0-255 components instead. Hue stays
/// a slider (a 1D property is naturally a slider), and Alpha/opacity stays a slider too.
/// Saturation (_s) and Value/brightness (_v) are now plain 0..1 fields owned by this
/// class instead of being read from now-removed Slider controls.
/// </summary>
public partial class ColorPickerField : UserControl
{
    public static readonly StyledProperty<string?> ColorHexProperty =
        AvaloniaProperty.Register<ColorPickerField, string?>(nameof(ColorHex), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public string? ColorHex
    {
        get => GetValue(ColorHexProperty);
        set => SetValue(ColorHexProperty, value);
    }

    /// <summary>Shared across every open ColorPickerField instance for the session --
    /// "recently used" reads naturally as one app-wide list, not one per field.</summary>
    private static readonly ObservableCollection<string> RecentColors = new();

    private Border _swatchPreview = null!;
    private Border _largePreview = null!;
    private TextBox _hexBox = null!;
    private Slider _hueSlider = null!;
    private Slider _alphaSlider = null!;
    private NumericUpDown _redBox = null!;
    private NumericUpDown _greenBox = null!;
    private NumericUpDown _blueBox = null!;
    private Button _transparentButton = null!;
    private ItemsControl _recentColorsList = null!;

    private Border _svBox = null!;
    private Canvas _svCanvas = null!;
    private SolidColorBrush _svHueBrush = null!;
    private Ellipse _svThumb = null!;
    private bool _svDragging;

    /// <summary>Current hue (0-359) and saturation/value (0-1), the authoritative state
    /// behind both the SV box thumb position and the Hue slider -- kept here rather than
    /// re-derived from Sliders every time, since Saturation/Value no longer have their
    /// own Slider controls after the Priority 9 rework.</summary>
    private double _hue;
    private double _s;
    private double _v;

    private bool _syncing;

    public ColorPickerField()
    {
        InitializeComponent();

        _swatchPreview = this.FindControl<Border>("SwatchPreview")!;
        _largePreview = this.FindControl<Border>("LargePreview")!;
        _hexBox = this.FindControl<TextBox>("HexBox")!;
        _hueSlider = this.FindControl<Slider>("HueSlider")!;
        _alphaSlider = this.FindControl<Slider>("AlphaSlider")!;
        _redBox = this.FindControl<NumericUpDown>("RedBox")!;
        _greenBox = this.FindControl<NumericUpDown>("GreenBox")!;
        _blueBox = this.FindControl<NumericUpDown>("BlueBox")!;
        _transparentButton = this.FindControl<Button>("TransparentButton")!;
        _recentColorsList = this.FindControl<ItemsControl>("RecentColorsList")!;
        _recentColorsList.ItemsSource = RecentColors;

        _svBox = this.FindControl<Border>("SvBox")!;
        _svCanvas = this.FindControl<Canvas>("SvCanvas")!;
        _svThumb = this.FindControl<Ellipse>("SvThumb")!;

        // SvHueBrush is created here rather than named in XAML: Avalonia's
        // FindControl<T> is constrained to Control-derived types, and a Brush is not a
        // Control, so it cannot be looked up by x:Name the same way the Border/Canvas/
        // Ellipse above are. Assigning our own SolidColorBrush instance directly to the
        // Canvas's Background achieves the same "named, mutable brush" result safely.
        _svHueBrush = new SolidColorBrush(Colors.Red);
        _svCanvas.Background = _svHueBrush;

        _hexBox.LostFocus += (_, _) => ApplyHexInput();
        _hexBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyHexInput(); };

        _hueSlider.ValueChanged += OnHueSliderChanged;
        _alphaSlider.ValueChanged += OnAlphaSliderChanged;

        _redBox.ValueChanged += OnRgbBoxChanged;
        _greenBox.ValueChanged += OnRgbBoxChanged;
        _blueBox.ValueChanged += OnRgbBoxChanged;

        _transparentButton.Click += (_, _) => SetColorHex("#00000000", commitToRecent: false);

        UpdateFromColorHex(ColorHex);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColorHexProperty && !_syncing)
        {
            UpdateFromColorHex(change.GetNewValue<string?>());
        }
    }

    private void OnRecentColorClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string hex }) SetColorHex(hex, commitToRecent: false);
    }

    // ------------------------------------------------------------ SV box (Priority 9) ----

    /// <summary>Default size used when computing a click/drag position before the
    /// control has actually been laid out yet (Bounds still 0x0 -- can happen for the
    /// very first pointer-pressed event inside a Flyout on some platforms). Matches the
    /// Border's own Height="120" in XAML; width falls back to the StackPanel's own
    /// Width="230" minus its Spacing/Border thickness, close enough for a single frame
    /// before real layout kicks in.</summary>
    private const double DefaultSvWidth = 226;
    private const double DefaultSvHeight = 120;

    private void OnSvBoxPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_svBox).Properties.IsLeftButtonPressed) return;
        _svDragging = true;
        e.Pointer.Capture(_svBox);
        ApplySvPointerPosition(e.GetPosition(_svBox));
    }

    private void OnSvBoxPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_svDragging) return;
        ApplySvPointerPosition(e.GetPosition(_svBox));
    }

    private void OnSvBoxPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_svDragging) return;
        _svDragging = false;
        e.Pointer.Capture(null);
        // Only record to "recently used" once the drag actually ends, not on every
        // intermediate frame while dragging -- same reasoning the old slider path used.
        AddToRecent((_hexBox.Text ?? ColorHex) ?? "#000000");
    }

    private void ApplySvPointerPosition(Point pointerPosInBox)
    {
        var width = _svBox.Bounds.Width > 0 ? _svBox.Bounds.Width : DefaultSvWidth;
        var height = _svBox.Bounds.Height > 0 ? _svBox.Bounds.Height : DefaultSvHeight;

        var s = Math.Clamp(pointerPosInBox.X / width, 0.0, 1.0);
        var v = Math.Clamp(1.0 - pointerPosInBox.Y / height, 0.0, 1.0);

        ApplyHsva(_hue, s, v, _alphaSlider.Value / 100.0, commitToRecent: false);
    }

    private void PositionSvThumb()
    {
        var width = _svBox.Bounds.Width > 0 ? _svBox.Bounds.Width : DefaultSvWidth;
        var height = _svBox.Bounds.Height > 0 ? _svBox.Bounds.Height : DefaultSvHeight;

        var x = _s * width;
        var y = (1.0 - _v) * height;

        Canvas.SetLeft(_svThumb, x - _svThumb.Width / 2);
        Canvas.SetTop(_svThumb, y - _svThumb.Height / 2);
    }

    // ------------------------------------------------------------ sliders / RGB ----

    private void OnHueSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncing) return;
        ApplyHsva(_hueSlider.Value, _s, _v, _alphaSlider.Value / 100.0, commitToRecent: false);
    }

    private void OnAlphaSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_syncing) return;
        ApplyHsva(_hue, _s, _v, _alphaSlider.Value / 100.0, commitToRecent: false);
    }

    private void OnRgbBoxChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_syncing) return;

        var r = (byte)Math.Clamp((int)(_redBox.Value ?? 0), 0, 255);
        var g = (byte)Math.Clamp((int)(_greenBox.Value ?? 0), 0, 255);
        var b = (byte)Math.Clamp((int)(_blueBox.Value ?? 0), 0, 255);
        var a = (byte)Math.Round(Math.Clamp(_alphaSlider.Value / 100.0, 0, 1) * 255);

        var color = new Color(a, r, g, b);
        UpdateFromColor(color);
        PushColorHex(color.ToString());
    }

    // ------------------------------------------------------------ hex <-> everything ----

    private void ApplyHexInput() => SetColorHex(_hexBox.Text ?? string.Empty, commitToRecent: true);

    /// <summary>Central "a new HSVA was chosen" entry point for the SV box and the
    /// Hue/Alpha sliders (RGB and hex have their own entry points below since they start
    /// from a Color, not HSVA components). Mirrors SetColorHex's role for hex input.</summary>
    private void ApplyHsva(double h, double s, double v, double a, bool commitToRecent)
    {
        var color = HsvaToColor(h, s, v, a);
        UpdateFromColor(color, skipHueAndSv: true);
        _hue = h;
        _s = s;
        _v = v;
        PositionSvThumb();
        PushColorHex(color.ToString());
        if (commitToRecent) AddToRecent(color.ToString());
    }

    /// <summary>Central "a new color was chosen" entry point for hex/RGB/recent/
    /// transparent -- validates, updates every dependent UI piece, pushes ColorHex, and
    /// (unless this came from a drag, where committing to "recent" on every intermediate
    /// frame would be noisy) records it in the recent-colors row.</summary>
    private void SetColorHex(string hex, bool commitToRecent)
    {
        Color color;
        try
        {
            color = Color.Parse(hex);
        }
        catch
        {
            // Invalid manual hex entry: leave the current valid state alone rather than
            // propagating garbage into the bound element (Part 68 -- no silent bad data).
            SetHexBoxText(ColorHex ?? "#000000");
            return;
        }

        UpdateFromColor(color);
        PushColorHex(color.ToString());
        if (commitToRecent) AddToRecent(color.ToString());
    }

    private void PushColorHex(string hex)
    {
        _syncing = true;
        try
        {
            ColorHex = hex;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void UpdateFromColorHex(string? hex)
    {
        Color color;
        try
        {
            color = Color.Parse(string.IsNullOrWhiteSpace(hex) ? "#000000" : hex);
        }
        catch
        {
            color = Colors.Black;
        }

        UpdateFromColor(color);
    }

    /// <summary>Refreshes every dependent visual piece (preview swatches, hex text, RGB
    /// boxes, Hue/Alpha sliders, SV thumb position and the SV box's hue-tinted
    /// background) from a Color. <paramref name="skipHueAndSv"/> is set by ApplyHsva
    /// (SV box drag / Hue slider / Alpha slider), which already owns the authoritative
    /// _hue/_s/_v and has already positioned the thumb itself -- re-deriving H/S/V from
    /// the resulting Color there would just reintroduce float round-trip jitter
    /// (e.g. saturation snapping to 0 near the grey diagonal) into fields the user is
    /// actively dragging.</summary>
    private void UpdateFromColor(Color color, bool skipHueAndSv = false)
    {
        _syncing = true;
        try
        {
            if (!skipHueAndSv)
            {
                var hsv = ColorToHsv(color);
                _hue = hsv.H;
                _s = hsv.S;
                _v = hsv.V;
                _hueSlider.Value = hsv.H;
                PositionSvThumb();
            }

            _alphaSlider.Value = color.A / 255.0 * 100.0;
            _redBox.Value = color.R;
            _greenBox.Value = color.G;
            _blueBox.Value = color.B;

            _svHueBrush.Color = HsvaToColor(_hue, 1, 1, 1);

            SetPreview(color);
            SetHexBoxText(color.ToString());
        }
        finally
        {
            _syncing = false;
        }
    }

    private void SetPreview(Color color)
    {
        var brush = new SolidColorBrush(color);
        _swatchPreview.Background = brush;
        _largePreview.Background = brush;
    }

    private void SetHexBoxText(string hex)
    {
        if (_hexBox.Text != hex) _hexBox.Text = hex;
    }

    private static void AddToRecent(string hex)
    {
        RecentColors.Remove(hex);
        RecentColors.Insert(0, hex);
        while (RecentColors.Count > 8) RecentColors.RemoveAt(RecentColors.Count - 1);
    }

    // ------------------------------------------------------------------- HSV math ----

    private static (double H, double S, double V) ColorToHsv(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        double h;
        if (delta < 0.00001) h = 0;
        else if (max == r) h = 60 * (((g - b) / delta) % 6);
        else if (max == g) h = 60 * ((b - r) / delta + 2);
        else h = 60 * ((r - g) / delta + 4);
        if (h < 0) h += 360;

        var s = max <= 0 ? 0 : delta / max;
        var v = max;
        return (h, s, v);
    }

    private static Color HsvaToColor(double h, double s, double v, double a)
    {
        h = ((h % 360) + 360) % 360;
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;

        var rgb = h switch
        {
            < 60 => (R: c, G: x, B: 0.0),
            < 120 => (R: x, G: c, B: 0.0),
            < 180 => (R: 0.0, G: c, B: x),
            < 240 => (R: 0.0, G: x, B: c),
            < 300 => (R: x, G: 0.0, B: c),
            _ => (R: c, G: 0.0, B: x),
        };

        return new Color(
            (byte)Math.Round(Math.Clamp(a, 0, 1) * 255),
            (byte)Math.Round(Math.Clamp(rgb.R + m, 0, 1) * 255),
            (byte)Math.Round(Math.Clamp(rgb.G + m, 0, 1) * 255),
            (byte)Math.Round(Math.Clamp(rgb.B + m, 0, 1) * 255));
    }
}
