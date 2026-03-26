using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WpfColor = System.Windows.Media.Color;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

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
    private const int PencilToggleHotKeyId = 41021;
    private const int UndoHotKeyId = 41022;
    private const int RedoHotKeyId = 41023;
    private const int WmHotKey = 0x0312;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private static readonly TimeSpan IdleShrinkDelay = TimeSpan.FromSeconds(5);
    private static readonly IntPtr HwndTopmost = new(-1);

    private WpfPoint _mouseDownPoint;
    private bool _isPointerDown;
    private bool _isDragging;
    private bool _wasMenuOpenOnPointerDown;
    private bool _isAdjustingBounds;
    private readonly DispatcherTimer _idleShrinkTimer = new();
    private readonly DispatcherTimer _topmostRefreshTimer = new();
    private DateTime _lastInteractionAtUtc = DateTime.UtcNow;
    private ToolbarWindow? _toolbarWindow;
    private DrawingOverlayWindow? _drawingOverlayWindow;
    private ScreenShareHubWindow? _screenShareHubWindow;
    private ActionMenuLayout _actionMenuLayout = ActionMenuLayout.Left;
    private HwndSource? _windowSource;
    private bool _isPencilToggleHotKeyRegistered;
    private bool _isUndoHotKeyRegistered;
    private bool _isRedoHotKeyRegistered;
    private bool _isHoveringMainUi;
    private bool _isHoveringToolbarUi;

    public MainWindow()
    {
        InitializeComponent();
        ActionMenuPopup.CustomPopupPlacementCallback = ActionMenuPopup_OnCustomPopupPlacement;
        _idleShrinkTimer.Interval = TimeSpan.FromMilliseconds(250);
        _idleShrinkTimer.Tick += IdleShrinkTimer_OnTick;
        _topmostRefreshTimer.Interval = TimeSpan.FromSeconds(1.5);
        _topmostRefreshTimer.Tick += TopmostRefreshTimer_OnTick;
        Loaded += MainWindow_OnLoaded;
        SourceInitialized += MainWindow_OnSourceInitialized;
        LocationChanged += MainWindow_OnLocationChanged;
        Deactivated += MainWindow_OnDeactivated;
        Closed += MainWindow_OnClosed;
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
        _topmostRefreshTimer.Start();
        EnforceOverlayWindowsOnTop();
    }

    private void MainWindow_OnLocationChanged(object? sender, EventArgs e)
    {
        ClampWindowToWorkArea();
        UpdateMenuPlacement();
        EnforceOverlayWindowsOnTop();
    }

    private void MainWindow_OnDeactivated(object? sender, EventArgs e)
    {
        ActionMenuPopup.IsOpen = false;
        _isHoveringMainUi = false;
        RecordInteraction(expandBadge: false);
        RefreshDrawingOverlayInteraction();
        EnforceOverlayWindowsOnTop();
    }

    private void MainWindow_OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowSource = (HwndSource?)PresentationSource.FromVisual(this);
        _windowSource?.AddHook(WindowMessageHook);
    }

    private void MainWindow_OnClosed(object? sender, EventArgs e)
    {
        UnregisterAnnotationHotKeys();
        _topmostRefreshTimer.Stop();

        if (_windowSource is not null)
        {
            _windowSource.RemoveHook(WindowMessageHook);
            _windowSource = null;
        }
    }

    private void MainBadge_OnMouseEnter(object sender, WpfMouseEventArgs e)
    {
        _isHoveringMainUi = true;
        RecordInteraction();
        RefreshDrawingOverlayInteraction();
    }

    private void MainBadge_OnMouseLeave(object sender, WpfMouseEventArgs e)
    {
        _isHoveringMainUi = false;
        RecordInteraction(expandBadge: false);
        RefreshDrawingOverlayInteraction();
    }

    private void ActionMenu_OnMouseEnter(object sender, WpfMouseEventArgs e)
    {
        _isHoveringMainUi = true;
        RecordInteraction();
        RefreshDrawingOverlayInteraction();
    }

    private void ActionMenu_OnMouseLeave(object sender, WpfMouseEventArgs e)
    {
        _isHoveringMainUi = false;
        RecordInteraction(expandBadge: false);
        RefreshDrawingOverlayInteraction();
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

    private void MainBadge_OnMouseMove(object sender, WpfMouseEventArgs e)
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
        _screenShareHubWindow?.Close();
        _toolbarWindow?.Close();
        _drawingOverlayWindow?.Close();
        Close();
    }

    private void StartSessionButton_OnClick(object sender, RoutedEventArgs e)
    {
        RecordInteraction();
        ActionMenuPopup.IsOpen = false;

        if (_screenShareHubWindow is null)
        {
            _screenShareHubWindow = new ScreenShareHubWindow();
            _screenShareHubWindow.Closed += (_, _) => _screenShareHubWindow = null;
        }

        if (!_screenShareHubWindow.IsVisible)
        {
            _screenShareHubWindow.Show();
        }

        _screenShareHubWindow.Activate();
    }

    private void ToolsButton_OnClick(object sender, RoutedEventArgs e)
    {
        RecordInteraction();
        ActionMenuPopup.IsOpen = false;
        _isHoveringMainUi = false;

        if (_toolbarWindow is { IsVisible: true })
        {
            _toolbarWindow.HideToolbar();
            return;
        }

        _toolbarWindow ??= CreateToolbarWindow();
        _toolbarWindow.ShowAtBottomCenter();
        _toolbarWindow.PositionWindow();
        _toolbarWindow.BringToFront();
        SyncAnnotationHotKeys();
        UpdateDrawingOverlay(_toolbarWindow.SelectedToolKind, shouldEnableInteraction: GetShouldEnableDrawingOverlay(_toolbarWindow.SelectedToolKind));
        EnforceOverlayWindowsOnTop();
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
        WpfSize popupSize,
        WpfSize targetSize,
        WpfPoint offset)
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
            new CustomPopupPlacement(new WpfPoint(horizontalOffset, verticalOffset), PopupPrimaryAxis.None)
        ];
    }

    private ToolbarWindow CreateToolbarWindow()
    {
        var toolbarWindow = new ToolbarWindow
        {
            Owner = this
        };

        toolbarWindow.SelectedToolChanged += ToolbarWindow_OnSelectedToolChanged;
        toolbarWindow.PencilSettingsChanged += ToolbarWindow_OnPencilSettingsChanged;
        toolbarWindow.HoverStateChanged += ToolbarWindow_OnHoverStateChanged;
        toolbarWindow.ToolbarHidden += ToolbarWindow_OnToolbarHidden;
        return toolbarWindow;
    }

    private DrawingOverlayWindow GetOrCreateDrawingOverlayWindow()
    {
        _drawingOverlayWindow ??= new DrawingOverlayWindow
        {
            Owner = this
        };
        return _drawingOverlayWindow;
    }

    private void ToolbarWindow_OnSelectedToolChanged(DrawingToolKind toolKind)
    {
        SyncAnnotationHotKeys();
        UpdateDrawingOverlay(toolKind, shouldEnableInteraction: GetShouldEnableDrawingOverlay(toolKind));
        _toolbarWindow?.BringToFront();
        EnforceOverlayWindowsOnTop();
    }

    private void ToolbarWindow_OnPencilSettingsChanged(WpfColor color, double thickness)
    {
        var drawingOverlayWindow = GetOrCreateDrawingOverlayWindow();
        drawingOverlayWindow.SetPencilStyle(color, thickness);
    }

    private void ToolbarWindow_OnHoverStateChanged(bool isHovering)
    {
        _isHoveringToolbarUi = isHovering;
        RefreshDrawingOverlayInteraction();
    }

    private void ToolbarWindow_OnToolbarHidden()
    {
        UnregisterAnnotationHotKeys();
        _isHoveringToolbarUi = false;
        UpdateDrawingOverlay(_toolbarWindow?.SelectedToolKind ?? DrawingToolKind.None, shouldEnableInteraction: false);
        EnforceOverlayWindowsOnTop();
    }

    private void UpdateDrawingOverlay(DrawingToolKind toolKind, bool shouldEnableInteraction)
    {
        var drawingOverlayWindow = GetOrCreateDrawingOverlayWindow();
        drawingOverlayWindow.SetTool(toolKind);
        drawingOverlayWindow.PositionWindow();
        drawingOverlayWindow.SetInteractionEnabled(shouldEnableInteraction);
        drawingOverlayWindow.BringToFront();
        EnforceOverlayWindowsOnTop();
    }

    private void RefreshDrawingOverlayInteraction()
    {
        var toolKind = _toolbarWindow?.SelectedToolKind ?? DrawingToolKind.None;
        UpdateDrawingOverlay(toolKind, shouldEnableInteraction: GetShouldEnableDrawingOverlay(toolKind));
    }

    private bool GetShouldEnableDrawingOverlay(DrawingToolKind toolKind)
    {
        if (_toolbarWindow is not { IsVisible: true })
        {
            return false;
        }

        if (_isHoveringMainUi || _isHoveringToolbarUi)
        {
            return false;
        }

        return toolKind.UsesDrawingOverlay();
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotKey)
        {
            return IntPtr.Zero;
        }

        var hotKeyId = wParam.ToInt32();

        if (hotKeyId == PencilToggleHotKeyId)
        {
            handled = true;
            TogglePencilFromHotKey();
            return IntPtr.Zero;
        }

        if (hotKeyId == UndoHotKeyId)
        {
            handled = true;
            UndoLastDrawing();
            return IntPtr.Zero;
        }

        if (hotKeyId == RedoHotKeyId)
        {
            handled = true;
            RedoLastDrawing();
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private void UndoLastDrawing()
    {
        if (_toolbarWindow is not { IsVisible: true } || _drawingOverlayWindow is null)
        {
            return;
        }

        _drawingOverlayWindow.UndoLastStroke();
        EnforceOverlayWindowsOnTop();
    }

    private void RedoLastDrawing()
    {
        if (_toolbarWindow is not { IsVisible: true } || _drawingOverlayWindow is null)
        {
            return;
        }

        _drawingOverlayWindow.RedoLastStroke();
        EnforceOverlayWindowsOnTop();
    }

    private void TopmostRefreshTimer_OnTick(object? sender, EventArgs e)
    {
        EnforceOverlayWindowsOnTop();
    }

    private void EnforceOverlayWindowsOnTop()
    {
        EnsureWindowIsTopmost(this);
        EnsureWindowIsTopmost(_toolbarWindow);
        EnsureWindowIsTopmost(_drawingOverlayWindow);
    }

    private static void EnsureWindowIsTopmost(Window? window)
    {
        if (window is null || !window.IsLoaded || !window.IsVisible)
        {
            return;
        }

        var handle = new WindowInteropHelper(window).Handle;

        if (handle == IntPtr.Zero)
        {
            return;
        }

        SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    private void TogglePencilFromHotKey()
    {
        if (_toolbarWindow is not { IsVisible: true })
        {
            return;
        }

        _toolbarWindow.TogglePencilMode();
    }

    private void SyncAnnotationHotKeys()
    {
        if (_toolbarWindow is not { IsVisible: true })
        {
            UnregisterAnnotationHotKeys();
            return;
        }

        RegisterHotKeyIfNeeded(ref _isPencilToggleHotKeyRegistered, PencilToggleHotKeyId, ModShift, Key.P);

        if (_toolbarWindow.SelectedToolKind.UsesDrawingOverlay())
        {
            RegisterHotKeyIfNeeded(ref _isUndoHotKeyRegistered, UndoHotKeyId, ModControl, Key.Z);
            RegisterHotKeyIfNeeded(ref _isRedoHotKeyRegistered, RedoHotKeyId, ModControl, Key.Y);
            return;
        }

        UnregisterHotKeyIfNeeded(ref _isUndoHotKeyRegistered, UndoHotKeyId);
        UnregisterHotKeyIfNeeded(ref _isRedoHotKeyRegistered, RedoHotKeyId);
    }

    private void UnregisterAnnotationHotKeys()
    {
        UnregisterHotKeyIfNeeded(ref _isPencilToggleHotKeyRegistered, PencilToggleHotKeyId);
        UnregisterHotKeyIfNeeded(ref _isUndoHotKeyRegistered, UndoHotKeyId);
        UnregisterHotKeyIfNeeded(ref _isRedoHotKeyRegistered, RedoHotKeyId);
    }

    private void RegisterHotKeyIfNeeded(ref bool isRegistered, int hotKeyId, uint modifiers, Key key)
    {
        if (isRegistered || _windowSource is null)
        {
            return;
        }

        var windowHandle = _windowSource.Handle;

        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        isRegistered = RegisterHotKey(
            windowHandle,
            hotKeyId,
            modifiers,
            (uint)KeyInterop.VirtualKeyFromKey(key));
    }

    private void UnregisterHotKeyIfNeeded(ref bool isRegistered, int hotKeyId)
    {
        if (!isRegistered || _windowSource is null)
        {
            return;
        }

        UnregisterHotKey(_windowSource.Handle, hotKeyId);
        isRegistered = false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
