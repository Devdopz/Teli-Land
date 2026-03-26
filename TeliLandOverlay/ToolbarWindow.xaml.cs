using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace TeliLandOverlay;

public partial class ToolbarWindow : Window
{
    private enum ToolFamily
    {
        Shape,
        Pen,
        Text
    }

    private enum ShapeToolOption
    {
        Rectangle,
        Line,
        Arrow,
        Ellipse,
        Polygon,
        Star
    }

    private enum PenToolOption
    {
        Pen,
        Pencil
    }

    private const double BottomMargin = 18;
    private const double EdgeSafeMargin = 5;

    private static readonly SolidColorBrush ActivePrimaryBackgroundBrush = CreateBrush("#2A9CFF");
    private static readonly SolidColorBrush ActiveDropdownBackgroundBrush = CreateBrush("#EAF4FF");
    private static readonly SolidColorBrush ActiveDropdownBorderBrush = CreateBrush("#D3E7FF");
    private static readonly SolidColorBrush ActiveDropdownForegroundBrush = CreateBrush("#237EE3");
    private static readonly SolidColorBrush DefaultPrimaryForegroundBrush = CreateBrush("#2A2A2A");
    private static readonly SolidColorBrush DefaultDropdownForegroundBrush = CreateBrush("#313131");

    private Point _dragStartScreenPoint;
    private double _dragStartLeft;
    private bool _isDragging;
    private bool _hasCustomHorizontalPosition;
    private ToolFamily _activeToolFamily = ToolFamily.Shape;
    private ShapeToolOption _selectedShapeTool = ShapeToolOption.Rectangle;
    private PenToolOption _selectedPenTool = PenToolOption.Pen;

    public ToolbarWindow()
    {
        InitializeComponent();
        Loaded += ToolbarWindow_OnLoaded;
        UpdateToolbarVisuals();
    }

    public void ShowAtBottomCenter()
    {
        CloseToolMenus();

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

    private void ToolbarWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionWindow();
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

    private void ShapeToolButton_OnClick(object sender, RoutedEventArgs e)
    {
        _activeToolFamily = ToolFamily.Shape;
        CloseToolMenus();
        UpdateToolbarVisuals();
    }

    private void PenToolButton_OnClick(object sender, RoutedEventArgs e)
    {
        _activeToolFamily = ToolFamily.Pen;
        CloseToolMenus();
        UpdateToolbarVisuals();
    }

    private void TextToolButton_OnClick(object sender, RoutedEventArgs e)
    {
        _activeToolFamily = ToolFamily.Text;
        CloseToolMenus();
        UpdateToolbarVisuals();
    }

    private void ShapeDropdownButton_OnClick(object sender, RoutedEventArgs e)
    {
        TogglePopup(ShapeMenuPopup, PenMenuPopup);
    }

    private void PenDropdownButton_OnClick(object sender, RoutedEventArgs e)
    {
        TogglePopup(PenMenuPopup, ShapeMenuPopup);
    }

    private void ShapeMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse(tag, out ShapeToolOption tool))
        {
            return;
        }

        _selectedShapeTool = tool;
        _activeToolFamily = ToolFamily.Shape;
        ShapeMenuPopup.IsOpen = false;
        UpdateToolbarVisuals();
    }

    private void PenMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse(tag, out PenToolOption tool))
        {
            return;
        }

        _selectedPenTool = tool;
        _activeToolFamily = ToolFamily.Pen;
        PenMenuPopup.IsOpen = false;
        UpdateToolbarVisuals();
    }

    private void CloseToolbarButton_OnClick(object sender, RoutedEventArgs e)
    {
        CloseToolMenus();
        Hide();
    }

    private void UpdateToolbarVisuals()
    {
        UpdateShapeIcon();
        UpdatePenIcon();
        UpdateButtonStates();
        UpdateSelectionMarks();
    }

    private void UpdateShapeIcon()
    {
        ShapeRectangleIcon.Visibility = _selectedShapeTool == ShapeToolOption.Rectangle ? Visibility.Visible : Visibility.Collapsed;
        ShapeLineIcon.Visibility = _selectedShapeTool == ShapeToolOption.Line ? Visibility.Visible : Visibility.Collapsed;
        ShapeArrowIcon.Visibility = _selectedShapeTool == ShapeToolOption.Arrow ? Visibility.Visible : Visibility.Collapsed;
        ShapeEllipseIcon.Visibility = _selectedShapeTool == ShapeToolOption.Ellipse ? Visibility.Visible : Visibility.Collapsed;
        ShapePolygonIcon.Visibility = _selectedShapeTool == ShapeToolOption.Polygon ? Visibility.Visible : Visibility.Collapsed;
        ShapeStarIcon.Visibility = _selectedShapeTool == ShapeToolOption.Star ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdatePenIcon()
    {
        PenIcon.Visibility = _selectedPenTool == PenToolOption.Pen ? Visibility.Visible : Visibility.Collapsed;
        PencilIcon.Visibility = _selectedPenTool == PenToolOption.Pencil ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateButtonStates()
    {
        ApplyToolFamilyState(ShapeToolButton, ShapeDropdownButton, _activeToolFamily == ToolFamily.Shape);
        ApplyToolFamilyState(PenToolButton, PenDropdownButton, _activeToolFamily == ToolFamily.Pen);
        ApplyToolFamilyState(TextToolButton, null, _activeToolFamily == ToolFamily.Text);
    }

    private static void ApplyToolFamilyState(Button primaryButton, Button? dropdownButton, bool isActive)
    {
        primaryButton.Background = isActive ? ActivePrimaryBackgroundBrush : Brushes.Transparent;
        primaryButton.BorderBrush = isActive ? ActivePrimaryBackgroundBrush : Brushes.Transparent;
        primaryButton.Foreground = isActive ? Brushes.White : DefaultPrimaryForegroundBrush;

        if (dropdownButton is null)
        {
            return;
        }

        dropdownButton.Background = isActive ? ActiveDropdownBackgroundBrush : Brushes.Transparent;
        dropdownButton.BorderBrush = isActive ? ActiveDropdownBorderBrush : Brushes.Transparent;
        dropdownButton.Foreground = isActive ? ActiveDropdownForegroundBrush : DefaultDropdownForegroundBrush;
    }

    private void UpdateSelectionMarks()
    {
        RectangleSelectionMark.Text = _selectedShapeTool == ShapeToolOption.Rectangle ? "✓" : string.Empty;
        LineSelectionMark.Text = _selectedShapeTool == ShapeToolOption.Line ? "✓" : string.Empty;
        ArrowSelectionMark.Text = _selectedShapeTool == ShapeToolOption.Arrow ? "✓" : string.Empty;
        EllipseSelectionMark.Text = _selectedShapeTool == ShapeToolOption.Ellipse ? "✓" : string.Empty;
        PolygonSelectionMark.Text = _selectedShapeTool == ShapeToolOption.Polygon ? "✓" : string.Empty;
        StarSelectionMark.Text = _selectedShapeTool == ShapeToolOption.Star ? "✓" : string.Empty;

        PenSelectionMark.Text = _selectedPenTool == PenToolOption.Pen ? "✓" : string.Empty;
        PencilSelectionMark.Text = _selectedPenTool == PenToolOption.Pencil ? "✓" : string.Empty;
    }

    private static void TogglePopup(Popup targetPopup, Popup otherPopup)
    {
        var shouldOpen = !targetPopup.IsOpen;
        otherPopup.IsOpen = false;
        targetPopup.IsOpen = shouldOpen;
    }

    private void CloseToolMenus()
    {
        ShapeMenuPopup.IsOpen = false;
        PenMenuPopup.IsOpen = false;
    }

    private static bool IsInteractiveElement(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is Button)
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
}
