using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace TeliLandOverlay;

public partial class ToolbarWindow : Window
{
    private const double BottomMargin = 18;
    private const double EdgeSafeMargin = 5;
    private static readonly SolidColorBrush ActiveToolBackgroundBrush = CreateBrush("#2A9CFF");
    private static readonly SolidColorBrush ActiveToolBorderBrush = CreateBrush("#2A9CFF");
    private static readonly SolidColorBrush ActiveToolForegroundBrush = Brushes.White;
    private static readonly SolidColorBrush InactiveToolBackgroundBrush = Brushes.Transparent;
    private static readonly SolidColorBrush InactiveToolBorderBrush = Brushes.Transparent;
    private static readonly SolidColorBrush InactiveToolForegroundBrush = CreateBrush("#2A2A2A");
    private static readonly SolidColorBrush UtilityButtonBorderBrush = CreateBrush("#D8E3EF");
    private static readonly SolidColorBrush UtilityButtonHoverBackgroundBrush = CreateBrush("#EEF4FB");
    private static readonly SolidColorBrush SelectedColorBorderBrush = CreateBrush("#FFFFFF");
    private static readonly SolidColorBrush DefaultColorBorderBrush = CreateBrush("#00000000");

    private Point _dragStartScreenPoint;
    private double _dragStartLeft;
    private bool _isDragging;
    private bool _hasCustomHorizontalPosition;
    private bool _isPencilEnabled;
    private Color _selectedPencilColor = (Color)ColorConverter.ConvertFromString("#1D2733")!;
    private double _selectedPencilThickness = 3;

    public event Action<DrawingToolKind>? SelectedToolChanged;
    public event Action<Color, double>? PencilSettingsChanged;
    public event Action<bool>? HoverStateChanged;
    public event Action? ToolbarHidden;

    public DrawingToolKind SelectedToolKind => _isPencilEnabled ? DrawingToolKind.Pencil : DrawingToolKind.None;

    public ToolbarWindow()
    {
        InitializeComponent();
        Loaded += ToolbarWindow_OnLoaded;
        ApplyPencilVisualState();
        ApplyColorVisualState();
        ApplySizeVisualState();
    }

    public void ShowAtBottomCenter()
    {
        CloseToolMenus();
        SetPencilEnabled(true, notifySelectionChanged: false);

        if (!IsLoaded)
        {
            Show();
            return;
        }

        PositionWindow();

        if (!IsVisible)
        {
            Show();
        }

        NotifySelectedToolChanged();
        NotifyPencilSettingsChanged();
    }

    public void HideToolbar()
    {
        if (!IsVisible)
        {
            return;
        }

        CloseToolMenus();
        SetPencilEnabled(false, notifySelectionChanged: false);
        Hide();
        ToolbarHidden?.Invoke();
    }

    public void BringToFront()
    {
        Topmost = false;
        Topmost = true;
    }

    public void PositionWindow()
    {
        var workArea = SystemParameters.WorkArea;
        var windowWidth = ActualWidth > 0 ? ActualWidth : Width;
        var windowHeight = ActualHeight > 0 ? ActualHeight : Height;

        var centeredLeft = workArea.Left + ((workArea.Width - windowWidth) / 2);
        var targetLeft = _hasCustomHorizontalPosition ? Left : centeredLeft;
        var minLeft = workArea.Left + EdgeSafeMargin;
        var maxLeft = Math.Max(minLeft, workArea.Right - windowWidth - EdgeSafeMargin);

        Left = Math.Clamp(targetLeft, minLeft, maxLeft);
        Top = Math.Max(workArea.Top + EdgeSafeMargin, workArea.Bottom - windowHeight - BottomMargin);
    }

    public void TogglePencilMode()
    {
        SetPencilEnabled(!_isPencilEnabled, notifySelectionChanged: true);
    }

    public void EnablePencil()
    {
        SetPencilEnabled(true, notifySelectionChanged: true);
    }

    private void ToolbarWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionWindow();
        ApplyColorVisualState();
        ApplySizeVisualState();
        NotifySelectedToolChanged();
        NotifyPencilSettingsChanged();
    }

    private void DragSurface_OnMouseEnter(object sender, MouseEventArgs e)
    {
        HoverStateChanged?.Invoke(true);
    }

    private void DragSurface_OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (ColorMenuPopup.IsOpen || SizeMenuPopup.IsOpen)
        {
            return;
        }

        HoverStateChanged?.Invoke(false);
    }

    private void PopupSurface_OnMouseEnter(object sender, MouseEventArgs e)
    {
        HoverStateChanged?.Invoke(true);
    }

    private void PopupSurface_OnMouseLeave(object sender, MouseEventArgs e)
    {
        HoverStateChanged?.Invoke(false);
    }

    private void ToolPopup_OnClosed(object sender, EventArgs e)
    {
        ApplyPencilVisualState();

        if (!IsMouseOver)
        {
            HoverStateChanged?.Invoke(false);
        }
    }

    private void DragSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsInteractiveElement(e.OriginalSource as DependencyObject))
        {
            return;
        }

        CloseToolMenus();
        _dragStartScreenPoint = PointToScreen(e.GetPosition(this));
        _dragStartLeft = Left;
        _isDragging = true;
        Mouse.Capture((IInputElement)sender);
        e.Handled = true;
    }

    private void DragSurface_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var currentScreenPoint = PointToScreen(e.GetPosition(this));
        var deltaX = currentScreenPoint.X - _dragStartScreenPoint.X;

        Left = _dragStartLeft + deltaX;
        _hasCustomHorizontalPosition = true;
        PositionWindow();
    }

    private void DragSurface_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        Mouse.Capture(null);
        PositionWindow();
        e.Handled = true;
    }

    private void PencilToolButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetPencilEnabled(true, notifySelectionChanged: true);
    }

    private void ColorPickerButton_OnClick(object sender, RoutedEventArgs e)
    {
        TogglePopup(ColorMenuPopup, SizeMenuPopup);
    }

    private void SizePickerButton_OnClick(object sender, RoutedEventArgs e)
    {
        TogglePopup(SizeMenuPopup, ColorMenuPopup);
    }

    private void ColorOptionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string colorHex })
        {
            return;
        }

        _selectedPencilColor = (Color)ColorConverter.ConvertFromString(colorHex)!;
        ApplyColorVisualState();
        ColorMenuPopup.IsOpen = false;
        NotifyPencilSettingsChanged();
    }

    private void SizeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _selectedPencilThickness = Math.Round(e.NewValue, 1);
        ApplySizeVisualState();

        if (IsLoaded)
        {
            NotifyPencilSettingsChanged();
        }
    }

    private void CloseToolbarButton_OnClick(object sender, RoutedEventArgs e)
    {
        HideToolbar();
    }

    private void NotifySelectedToolChanged()
    {
        SelectedToolChanged?.Invoke(SelectedToolKind);
    }

    private void NotifyPencilSettingsChanged()
    {
        PencilSettingsChanged?.Invoke(_selectedPencilColor, _selectedPencilThickness);
    }

    private void SetPencilEnabled(bool isEnabled, bool notifySelectionChanged)
    {
        if (_isPencilEnabled == isEnabled)
        {
            if (notifySelectionChanged)
            {
                NotifySelectedToolChanged();
            }

            return;
        }

        _isPencilEnabled = isEnabled;
        ApplyPencilVisualState();

        if (notifySelectionChanged)
        {
            NotifySelectedToolChanged();
        }
    }

    private void ApplyPencilVisualState()
    {
        PencilToolButton.Background = _isPencilEnabled ? ActiveToolBackgroundBrush : InactiveToolBackgroundBrush;
        PencilToolButton.BorderBrush = _isPencilEnabled ? ActiveToolBorderBrush : InactiveToolBorderBrush;
        PencilToolButton.Foreground = _isPencilEnabled ? ActiveToolForegroundBrush : InactiveToolForegroundBrush;
        ColorPickerButton.BorderBrush = ColorMenuPopup.IsOpen ? UtilityButtonBorderBrush : InactiveToolBorderBrush;
        ColorPickerButton.Background = ColorMenuPopup.IsOpen ? UtilityButtonHoverBackgroundBrush : InactiveToolBackgroundBrush;
        SizePickerButton.BorderBrush = SizeMenuPopup.IsOpen ? UtilityButtonBorderBrush : InactiveToolBorderBrush;
        SizePickerButton.Background = SizeMenuPopup.IsOpen ? UtilityButtonHoverBackgroundBrush : InactiveToolBackgroundBrush;
    }

    private void ApplyColorVisualState()
    {
        CurrentColorSwatch.Background = new SolidColorBrush(_selectedPencilColor);
        var selectedColorHex = ToRgbHex(_selectedPencilColor);

        foreach (var child in ColorPalettePanel.Children)
        {
            if (child is not Button { Tag: string colorHex, Content: Border colorBorder })
            {
                continue;
            }

            var isSelected = string.Equals(colorHex, selectedColorHex, StringComparison.OrdinalIgnoreCase);
            colorBorder.BorderThickness = isSelected ? new Thickness(2) : new Thickness(0);
            colorBorder.BorderBrush = isSelected ? SelectedColorBorderBrush : DefaultColorBorderBrush;
        }
    }

    private void ApplySizeVisualState()
    {
        SizeValueText.Text = $"{Math.Round(_selectedPencilThickness, 1):0.#} px";
        SizePreviewLine.Height = _selectedPencilThickness;
        SizePreviewLine.RadiusX = _selectedPencilThickness / 2;
        SizePreviewLine.RadiusY = _selectedPencilThickness / 2;

        if (Math.Abs(SizeSlider.Value - _selectedPencilThickness) > 0.01)
        {
            SizeSlider.Value = _selectedPencilThickness;
        }
    }

    private void TogglePopup(Popup targetPopup, Popup otherPopup)
    {
        var shouldOpen = !targetPopup.IsOpen;
        otherPopup.IsOpen = false;
        targetPopup.IsOpen = shouldOpen;
        ApplyPencilVisualState();
        HoverStateChanged?.Invoke(shouldOpen || IsMouseOver);
    }

    private void CloseToolMenus()
    {
        ColorMenuPopup.IsOpen = false;
        SizeMenuPopup.IsOpen = false;
        ApplyPencilVisualState();
    }

    private static bool IsInteractiveElement(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is ButtonBase or Slider or Thumb)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    private static string ToRgbHex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }
}
