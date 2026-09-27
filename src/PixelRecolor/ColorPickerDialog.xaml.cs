using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MediaColor = System.Windows.Media.Color;
using MediaLinearGradientBrush = System.Windows.Media.LinearGradientBrush;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;
using WpfCanvas = System.Windows.Controls.Canvas;
using WpfKeyboardFocusChangedEventArgs = System.Windows.Input.KeyboardFocusChangedEventArgs;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfMouseButtonState = System.Windows.Input.MouseButtonState;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;

namespace PixelRecolor;

/// <summary>
/// カラーピッカー、HEX入力、RGBA入力を1つにまとめた色選択ダイアログです。
/// </summary>
public partial class ColorPickerDialog : Window
{
    // ダイアログを開き直したときも、最後に選んだ入力方式を引き継ぎます。
    private static ColorInputMode _lastInputMode = ColorInputMode.Hex;

    private MediaColor _selectedColor;
    private double _hue;
    private double _saturation;
    private double _value;
    private bool _isUpdatingControls = true;
    private bool _canRaiseChanges;
    private PickerPart _activePicker;

    public ColorPickerDialog(MediaColor initialColor)
    {
        _selectedColor = initialColor;
        RgbToHsv(initialColor.R, initialColor.G, initialColor.B,
            out _hue, out _saturation, out _value);

        InitializeComponent();
        _isUpdatingControls = false;
        SetInputMode(_lastInputMode);
        UpdateAllControls();
        Loaded += ColorPickerDialog_Loaded;
    }

    public MediaColor SelectedColor => _selectedColor;

    /// <summary>
    /// ピッカーまたは有効な数値入力で選択色が変わるたびに発生します。
    /// </summary>
    public event Action<MediaColor>? SelectedColorChanged;

    private void ColorPickerDialog_Loaded(object sender, RoutedEventArgs e)
    {
        _canRaiseChanges = true;
        UpdatePickerVisuals();
        FocusActiveInput();
    }

    private void SaturationValuePicker_MouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
    {
        BeginPickerDrag(PickerPart.SaturationValue, SaturationValuePicker, e.GetPosition(SaturationValuePicker));
        e.Handled = true;
    }

    private void SaturationValuePicker_MouseMove(object sender, WpfMouseEventArgs e)
    {
        ContinuePickerDrag(PickerPart.SaturationValue, SaturationValuePicker,
            e.GetPosition(SaturationValuePicker), e.LeftButton);
    }

    private void HuePicker_MouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
    {
        BeginPickerDrag(PickerPart.Hue, HuePicker, e.GetPosition(HuePicker));
        e.Handled = true;
    }

    private void HuePicker_MouseMove(object sender, WpfMouseEventArgs e)
    {
        ContinuePickerDrag(PickerPart.Hue, HuePicker, e.GetPosition(HuePicker), e.LeftButton);
    }

    private void AlphaPicker_MouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
    {
        BeginPickerDrag(PickerPart.Alpha, AlphaPicker, e.GetPosition(AlphaPicker));
        e.Handled = true;
    }

    private void AlphaPicker_MouseMove(object sender, WpfMouseEventArgs e)
    {
        ContinuePickerDrag(PickerPart.Alpha, AlphaPicker, e.GetPosition(AlphaPicker), e.LeftButton);
    }

    private void Picker_MouseLeftButtonUp(object sender, WpfMouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.IsMouseCaptured)
        {
            element.ReleaseMouseCapture();
        }

        _activePicker = PickerPart.None;
        e.Handled = true;
    }

    private void Picker_LostMouseCapture(object sender, WpfMouseEventArgs e)
    {
        _activePicker = PickerPart.None;
    }

    private void BeginPickerDrag(PickerPart picker, FrameworkElement element, WpfPoint position)
    {
        _activePicker = picker;
        element.CaptureMouse();
        UpdateFromPicker(picker, element, position);
    }

    private void ContinuePickerDrag(
        PickerPart picker,
        FrameworkElement element,
        WpfPoint position,
        WpfMouseButtonState leftButton)
    {
        if (_activePicker != picker || leftButton != WpfMouseButtonState.Pressed)
        {
            return;
        }

        UpdateFromPicker(picker, element, position);
    }

    private void UpdateFromPicker(PickerPart picker, FrameworkElement element, WpfPoint position)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return;
        }

        var x = Math.Clamp(position.X, 0, element.ActualWidth);
        var y = Math.Clamp(position.Y, 0, element.ActualHeight);

        switch (picker)
        {
            case PickerPart.SaturationValue:
                _saturation = x / element.ActualWidth;
                _value = 1 - (y / element.ActualHeight);
                break;
            case PickerPart.Hue:
                _hue = x / element.ActualWidth * 360;
                if (_hue >= 360)
                {
                    _hue = 0;
                }

                break;
            case PickerPart.Alpha:
                var alpha = (byte)Math.Round(x / element.ActualWidth * 255);
                _selectedColor = MediaColor.FromArgb(alpha, _selectedColor.R, _selectedColor.G, _selectedColor.B);
                break;
        }

        if (picker is PickerPart.SaturationValue or PickerPart.Hue)
        {
            var rgb = HsvToRgb(_hue, _saturation, _value);
            _selectedColor = MediaColor.FromArgb(_selectedColor.A, rgb.R, rgb.G, rgb.B);
        }

        UpdateAllControls();
        RaiseSelectedColorChanged();
    }

    private void InputModeRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingControls)
        {
            return;
        }

        var mode = RgbaModeRadioButton.IsChecked == true
            ? ColorInputMode.Rgba
            : ColorInputMode.Hex;
        SetInputMode(mode);
        UpdateAllControls();
        FocusActiveInput();
    }

    private void HexTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingControls)
        {
            return;
        }

        if (!TryParseHexColor(HexTextBox.Text, _selectedColor.A, out var color))
        {
            ValidationMessageTextBlock.Text = "HEXは #RRGGBB または #RRGGBBAA で入力してください。";
            return;
        }

        ApplyTypedColor(color, ColorInputMode.Hex);
    }

    private void HexTextBox_LostKeyboardFocus(object sender, WpfKeyboardFocusChangedEventArgs e)
    {
        if (TryParseHexColor(HexTextBox.Text, _selectedColor.A, out _))
        {
            UpdateAllControls();
        }
    }

    private void RgbaTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingControls)
        {
            return;
        }

        if (!TryParseByte(RedTextBox.Text, out var red)
            || !TryParseByte(GreenTextBox.Text, out var green)
            || !TryParseByte(BlueTextBox.Text, out var blue)
            || !TryParseByte(AlphaTextBox.Text, out var alpha))
        {
            ValidationMessageTextBlock.Text = "RGBAはそれぞれ0～255で入力してください。";
            return;
        }

        ApplyTypedColor(MediaColor.FromArgb(alpha, red, green, blue), ColorInputMode.Rgba);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryApplyActiveInput())
        {
            return;
        }

        DialogResult = true;
    }

    private bool TryApplyActiveInput()
    {
        if (_lastInputMode == ColorInputMode.Hex)
        {
            if (!TryParseHexColor(HexTextBox.Text, _selectedColor.A, out var hexColor))
            {
                ValidationMessageTextBlock.Text = "HEXは #RRGGBB または #RRGGBBAA で入力してください。";
                HexTextBox.Focus();
                return false;
            }

            ApplyTypedColor(hexColor, ColorInputMode.Hex);
            return true;
        }

        if (!TryParseByte(RedTextBox.Text, out var red)
            || !TryParseByte(GreenTextBox.Text, out var green)
            || !TryParseByte(BlueTextBox.Text, out var blue)
            || !TryParseByte(AlphaTextBox.Text, out var alpha))
        {
            ValidationMessageTextBlock.Text = "RGBAはそれぞれ0～255で入力してください。";
            RedTextBox.Focus();
            return false;
        }

        ApplyTypedColor(MediaColor.FromArgb(alpha, red, green, blue), ColorInputMode.Rgba);
        return true;
    }

    private void ApplyTypedColor(MediaColor color, ColorInputMode sourceMode)
    {
        _selectedColor = color;
        RgbToHsv(color.R, color.G, color.B, out _hue, out _saturation, out _value);

        _isUpdatingControls = true;
        if (sourceMode == ColorInputMode.Hex)
        {
            UpdateRgbaTextBoxes();
        }
        else
        {
            HexTextBox.Text = ToHex(color);
        }

        _isUpdatingControls = false;
        ValidationMessageTextBlock.Text = string.Empty;
        UpdatePickerVisuals();
        RaiseSelectedColorChanged();
    }

    private void RaiseSelectedColorChanged()
    {
        if (_canRaiseChanges)
        {
            SelectedColorChanged?.Invoke(_selectedColor);
        }
    }

    private void SetInputMode(ColorInputMode mode)
    {
        _lastInputMode = mode;
        _isUpdatingControls = true;
        HexModeRadioButton.IsChecked = mode == ColorInputMode.Hex;
        RgbaModeRadioButton.IsChecked = mode == ColorInputMode.Rgba;
        HexInputPanel.Visibility = mode == ColorInputMode.Hex ? Visibility.Visible : Visibility.Collapsed;
        RgbaInputPanel.Visibility = mode == ColorInputMode.Rgba ? Visibility.Visible : Visibility.Collapsed;
        _isUpdatingControls = false;
    }

    private void FocusActiveInput()
    {
        if (_lastInputMode == ColorInputMode.Hex)
        {
            HexTextBox.Focus();
            HexTextBox.SelectAll();
            return;
        }

        RedTextBox.Focus();
        RedTextBox.SelectAll();
    }

    private void UpdateAllControls()
    {
        _isUpdatingControls = true;
        HexTextBox.Text = ToHex(_selectedColor);
        UpdateRgbaTextBoxes();
        _isUpdatingControls = false;
        ValidationMessageTextBlock.Text = string.Empty;
        UpdatePickerVisuals();
    }

    private void UpdateRgbaTextBoxes()
    {
        RedTextBox.Text = _selectedColor.R.ToString(CultureInfo.InvariantCulture);
        GreenTextBox.Text = _selectedColor.G.ToString(CultureInfo.InvariantCulture);
        BlueTextBox.Text = _selectedColor.B.ToString(CultureInfo.InvariantCulture);
        AlphaTextBox.Text = _selectedColor.A.ToString(CultureInfo.InvariantCulture);
    }

    private void UpdatePickerVisuals()
    {
        var hueColor = HsvToRgb(_hue, 1, 1);
        HueColorBorder.Background = new MediaSolidColorBrush(hueColor);

        AlphaGradientBorder.Background = new MediaLinearGradientBrush(
            MediaColor.FromArgb(0, _selectedColor.R, _selectedColor.G, _selectedColor.B),
            MediaColor.FromArgb(255, _selectedColor.R, _selectedColor.G, _selectedColor.B),
            new WpfPoint(0, 0.5),
            new WpfPoint(1, 0.5));

        if (!IsLoaded)
        {
            return;
        }

        PositionThumb(SaturationValueThumb,
            _saturation * SaturationValuePicker.ActualWidth,
            (1 - _value) * SaturationValuePicker.ActualHeight);
        PositionThumb(HueThumb, _hue / 360 * HuePicker.ActualWidth, HuePicker.ActualHeight / 2);
        PositionThumb(AlphaThumb, _selectedColor.A / 255.0 * AlphaPicker.ActualWidth, AlphaPicker.ActualHeight / 2);
    }

    private static void PositionThumb(FrameworkElement thumb, double centerX, double centerY)
    {
        WpfCanvas.SetLeft(thumb, centerX - thumb.Width / 2);
        WpfCanvas.SetTop(thumb, centerY - thumb.Height / 2);
    }

    private static bool TryParseHexColor(string? text, byte currentAlpha, out MediaColor color)
    {
        color = default;
        var value = text?.Trim().TrimStart('#') ?? string.Empty;
        if (value.Length is not (6 or 8)
            || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        if (value.Length == 6)
        {
            color = MediaColor.FromArgb(
                currentAlpha,
                (byte)(parsed >> 16),
                (byte)(parsed >> 8),
                (byte)parsed);
            return true;
        }

        color = MediaColor.FromArgb(
            (byte)parsed,
            (byte)(parsed >> 24),
            (byte)(parsed >> 16),
            (byte)(parsed >> 8));
        return true;
    }

    private static bool TryParseByte(string? text, out byte value)
    {
        return byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string ToHex(MediaColor color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";
    }

    private static MediaColor HsvToRgb(double hue, double saturation, double value)
    {
        hue = (hue % 360 + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);

        var chroma = value * saturation;
        var section = hue / 60;
        var x = chroma * (1 - Math.Abs(section % 2 - 1));
        var match = value - chroma;

        (double red, double green, double blue) = section switch
        {
            < 1 => (chroma, x, 0d),
            < 2 => (x, chroma, 0d),
            < 3 => (0d, chroma, x),
            < 4 => (0d, x, chroma),
            < 5 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };

        return MediaColor.FromRgb(
            (byte)Math.Round((red + match) * 255),
            (byte)Math.Round((green + match) * 255),
            (byte)Math.Round((blue + match) * 255));
    }

    private static void RgbToHsv(
        byte red,
        byte green,
        byte blue,
        out double hue,
        out double saturation,
        out double value)
    {
        var r = red / 255.0;
        var g = green / 255.0;
        var b = blue / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        if (delta == 0)
        {
            hue = 0;
        }
        else if (max == r)
        {
            hue = 60 * (((g - b) / delta) % 6);
        }
        else if (max == g)
        {
            hue = 60 * (((b - r) / delta) + 2);
        }
        else
        {
            hue = 60 * (((r - g) / delta) + 4);
        }

        if (hue < 0)
        {
            hue += 360;
        }

        saturation = max == 0 ? 0 : delta / max;
        value = max;
    }

    private enum ColorInputMode
    {
        Hex,
        Rgba
    }

    private enum PickerPart
    {
        None,
        SaturationValue,
        Hue,
        Alpha
    }
}
