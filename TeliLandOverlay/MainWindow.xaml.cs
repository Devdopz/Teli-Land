using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace TeliLandOverlay;

public partial class MainWindow : Window
{
    private const double ScreenMargin = 24;

    private Point _mouseDownPoint;
    private bool _isPointerDown;
    private bool _isDragging;
    private bool _wasMenuOpenOnPointerDown;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_OnLoaded;
        LocationChanged += MainWindow_OnLocationChanged;
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        Left = Math.Max(workArea.Left + ScreenMargin, workArea.Right - Width - ScreenMargin);
        Top = Math.Max(workArea.Top + ScreenMargin, workArea.Bottom - Height - ScreenMargin);
        UpdateMenuPlacement();
    }

    private void MainWindow_OnLocationChanged(object? sender, EventArgs e)
    {
        UpdateMenuPlacement();
    }

    private void MainBadge_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mouseDownPoint = e.GetPosition(this);
        _isPointerDown = true;
        _isDragging = false;
        _wasMenuOpenOnPointerDown = ActionMenuPopup.IsOpen;
        MainBadgeHost.CaptureMouse();
        e.Handled = true;
    }

    private void MainBadge_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPointerDown || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var currentPoint = e.GetPosition(this);
        var movedFarEnough =
            Math.Abs(currentPoint.X - _mouseDownPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(currentPoint.Y - _mouseDownPoint.Y) > SystemParameters.MinimumVerticalDragDistance;

        if (!movedFarEnough)
        {
            return;
        }

        _isDragging = true;
        _isPointerDown = false;
        ActionMenuPopup.IsOpen = false;
        MainBadgeHost.ReleaseMouseCapture();
        DragMove();
        UpdateMenuPlacement();
    }

    private void MainBadge_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        MainBadgeHost.ReleaseMouseCapture();

        if (_isPointerDown && !_isDragging)
        {
            ActionMenuPopup.IsOpen = !_wasMenuOpenOnPointerDown;
        }

        _isPointerDown = false;
        _isDragging = false;
        _wasMenuOpenOnPointerDown = false;
        e.Handled = true;
    }

    private void UpdateMenuPlacement()
    {
        if (!IsLoaded)
        {
            return;
        }

        var workArea = SystemParameters.WorkArea;
        var windowWidth = ActualWidth > 0 ? ActualWidth : Width;
        var windowCenterX = Left + (windowWidth / 2);
        var workAreaCenterX = workArea.Left + (workArea.Width / 2);
        var openOnLeft = windowCenterX > workAreaCenterX;

        ActionMenuPopup.Placement = openOnLeft ? PlacementMode.Left : PlacementMode.Right;
        ActionMenuPopup.HorizontalOffset = openOnLeft ? -8 : 8;
        ActionMenuPopup.VerticalOffset = 0;
    }

    private void ExitOverlay_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
