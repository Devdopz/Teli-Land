using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

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
    private const double NormalBadgeScale = 1;
    private const double CompactBadgeScale = 0.68;
    private static readonly TimeSpan IdleShrinkDelay = TimeSpan.FromSeconds(5);

    private Point _mouseDownPoint;
    private bool _isPointerDown;
    private bool _isDragging;
    private bool _wasMenuOpenOnPointerDown;
    private bool _isAdjustingBounds;
    private readonly DispatcherTimer _idleShrinkTimer = new();
    private DateTime _lastInteractionAtUtc = DateTime.UtcNow;
    private ActionMenuLayout _actionMenuLayout = ActionMenuLayout.Left;

    public MainWindow()
    {
        InitializeComponent();
        ActionMenuPopup.CustomPopupPlacementCallback = ActionMenuPopup_OnCustomPopupPlacement;
        _idleShrinkTimer.Interval = TimeSpan.FromMilliseconds(250);
        _idleShrinkTimer.Tick += IdleShrinkTimer_OnTick;
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
        RecordInteraction(expandBadge: false);
        _idleShrinkTimer.Start();
    }

    private void MainWindow_OnLocationChanged(object? sender, EventArgs e)
    {
        ClampWindowToWorkArea();
        UpdateMenuPlacement();
    }

    private void MainWindow_OnDeactivated(object? sender, EventArgs e)
    {
        ActionMenuPopup.IsOpen = false;
        RecordInteraction(expandBadge: false);
    }

    private void MainBadge_OnMouseEnter(object sender, MouseEventArgs e)
    {
        RecordInteraction();
    }

    private void MainBadge_OnMouseLeave(object sender, MouseEventArgs e)
    {
        RecordInteraction(expandBadge: false);
    }

    private void ActionMenu_OnMouseEnter(object sender, MouseEventArgs e)
    {
        RecordInteraction();
    }

    private void ActionMenu_OnMouseLeave(object sender, MouseEventArgs e)
    {
        RecordInteraction(expandBadge: false);
    }

    private void MainBadge_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        RecordInteraction();
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
        RecordInteraction();
        ActionMenuPopup.IsOpen = false;
        MainBadgeHost.ReleaseMouseCapture();
        DragMove();
        _isDragging = false;
        UpdateMenuPlacement();
        RecordInteraction(expandBadge: false);
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
        RecordInteraction();

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

    private void IdleShrinkTimer_OnTick(object? sender, EventArgs e)
    {
        if (DateTime.UtcNow - _lastInteractionAtUtc < IdleShrinkDelay)
        {
            return;
        }

        if (MainBadgeHost.IsMouseOver || VerticalMenuPanel.IsMouseOver || HorizontalMenuPanel.IsMouseOver || _isDragging)
        {
            return;
        }

        ActionMenuPopup.IsOpen = false;
        AnimateBadgeScale(CompactBadgeScale);
    }

    private void RecordInteraction(bool expandBadge = true)
    {
        _lastInteractionAtUtc = DateTime.UtcNow;

        if (expandBadge)
        {
            ExpandBadge();
        }
    }

    private void ExpandBadge()
    {
        AnimateBadgeScale(NormalBadgeScale);
    }

    private void AnimateBadgeScale(double targetScale)
    {
        if (Math.Abs(MainBadgeScaleTransform.ScaleX - targetScale) < 0.001 &&
            Math.Abs(MainBadgeScaleTransform.ScaleY - targetScale) < 0.001)
        {
            return;
        }

        var animation = new DoubleAnimation
        {
            To = targetScale,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new QuadraticEase
            {
                EasingMode = EasingMode.EaseOut
            }
        };

        MainBadgeScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        MainBadgeScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
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
