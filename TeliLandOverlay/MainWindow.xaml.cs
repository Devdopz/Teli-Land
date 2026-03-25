using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace TeliLandOverlay;

public partial class MainWindow : Window
{
    private enum ActionMenuLayout
    {
        Left,
        Right,
        Top,
        Bottom
    }

    private const double StartupMargin = 24;
    private const double EdgeSafeMargin = 5;
    private const double MenuGap = 8;

    private Point _mouseDownPoint;
    private bool _isPointerDown;
    private bool _isDragging;
    private bool _wasMenuOpenOnPointerDown;
    private bool _isAdjustingBounds;
    private ActionMenuLayout _actionMenuLayout = ActionMenuLayout.Left;

    public MainWindow()
    {
        InitializeComponent();
        ActionMenuPopup.CustomPopupPlacementCallback = ActionMenuPopup_OnCustomPopupPlacement;
        Loaded += MainWindow_OnLoaded;
        LocationChanged += MainWindow_OnLocationChanged;
        Deactivated += MainWindow_OnDeactivated;
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        Left = Math.Max(workArea.Left + StartupMargin, workArea.Right - Width - StartupMargin);
        Top = Math.Max(workArea.Top + StartupMargin, workArea.Bottom - Height - StartupMargin);
        ClampWindowToWorkArea();
        UpdateMenuPlacement();
    }

    private void MainWindow_OnLocationChanged(object? sender, EventArgs e)
    {
        ClampWindowToWorkArea();
        UpdateMenuPlacement();
    }

    private void MainWindow_OnDeactivated(object? sender, EventArgs e)
    {
        ActionMenuPopup.IsOpen = false;
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
        var windowHeight = ActualHeight > 0 ? ActualHeight : Height;
        var windowCenterX = Left + (windowWidth / 2);
        var windowCenterY = Top + (windowHeight / 2);
        var workAreaCenterX = workArea.Left + (workArea.Width / 2);
        var workAreaCenterY = workArea.Top + (workArea.Height / 2);
        var horizontalDistance = Math.Abs(windowCenterX - workAreaCenterX) / workArea.Width;
        var verticalDistance = Math.Abs(windowCenterY - workAreaCenterY) / workArea.Height;

        _actionMenuLayout = horizontalDistance >= verticalDistance
            ? windowCenterX > workAreaCenterX ? ActionMenuLayout.Left : ActionMenuLayout.Right
            : windowCenterY > workAreaCenterY ? ActionMenuLayout.Top : ActionMenuLayout.Bottom;

        var useVerticalMenu = _actionMenuLayout is ActionMenuLayout.Left or ActionMenuLayout.Right;
        VerticalMenuPanel.Visibility = useVerticalMenu ? Visibility.Visible : Visibility.Collapsed;
        HorizontalMenuPanel.Visibility = useVerticalMenu ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ClampWindowToWorkArea()
    {
        if (!IsLoaded || _isAdjustingBounds)
        {
            return;
        }

        var workArea = SystemParameters.WorkArea;
        var windowWidth = ActualWidth > 0 ? ActualWidth : Width;
        var windowHeight = ActualHeight > 0 ? ActualHeight : Height;

        var minLeft = workArea.Left + EdgeSafeMargin;
        var maxLeft = Math.Max(minLeft, workArea.Right - windowWidth - EdgeSafeMargin);
        var minTop = workArea.Top + EdgeSafeMargin;
        var maxTop = Math.Max(minTop, workArea.Bottom - windowHeight - EdgeSafeMargin);

        var clampedLeft = Math.Clamp(Left, minLeft, maxLeft);
        var clampedTop = Math.Clamp(Top, minTop, maxTop);

        if (Math.Abs(clampedLeft - Left) < double.Epsilon && Math.Abs(clampedTop - Top) < double.Epsilon)
        {
            return;
        }

        _isAdjustingBounds = true;
        Left = clampedLeft;
        Top = clampedTop;
        _isAdjustingBounds = false;
    }

    private void ExitOverlay_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private CustomPopupPlacement[] ActionMenuPopup_OnCustomPopupPlacement(
        Size popupSize,
        Size targetSize,
        Point offset)
    {
        var horizontalOffset = _actionMenuLayout switch
        {
            ActionMenuLayout.Left => -(popupSize.Width + MenuGap),
            ActionMenuLayout.Right => targetSize.Width + MenuGap,
            _ => (targetSize.Width - popupSize.Width) / 2
        };

        var verticalOffset = _actionMenuLayout switch
        {
            ActionMenuLayout.Top => -(popupSize.Height + MenuGap),
            ActionMenuLayout.Bottom => targetSize.Height + MenuGap,
            _ => (targetSize.Height - popupSize.Height) / 2
        };

        return
        [
            new CustomPopupPlacement(new Point(horizontalOffset, verticalOffset), PopupPrimaryAxis.None)
        ];
    }
}
