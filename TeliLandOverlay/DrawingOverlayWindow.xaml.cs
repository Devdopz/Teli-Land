using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Collections.Generic;

namespace TeliLandOverlay;

public partial class DrawingOverlayWindow : Window
{
    private const double MinimumStrokeLength = 4;
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private static readonly SolidColorBrush InteractionCaptureBrush = CreateBrush("#01000000");

    private DrawingToolKind _activeTool = DrawingToolKind.None;
    private bool _isDrawing;
    private Polyline? _activeStroke;
    private Color _pencilColor = (Color)ColorConverter.ConvertFromString("#1D2733")!;
    private double _pencilThickness = 3;
    private readonly Stack<UIElement> _redoStrokes = new();
    private IntPtr _windowHandle;
    private bool _isClickThroughEnabled = true;

    public DrawingOverlayWindow()
    {
        InitializeComponent();
        Loaded += DrawingOverlayWindow_OnLoaded;
        SourceInitialized += DrawingOverlayWindow_OnSourceInitialized;
    }

    public bool HasDrawings => DrawingCanvas.Children.Count > 0;

    public void SetTool(DrawingToolKind toolKind)
    {
        _activeTool = toolKind;
        InteractionSurface.Cursor = toolKind == DrawingToolKind.Pencil ? Cursors.Pen : Cursors.Arrow;
    }

    public void SetPencilStyle(Color color, double thickness)
    {
        _pencilColor = color;
        _pencilThickness = Math.Clamp(thickness, 1.5, 12);

        if (_activeStroke is not null)
        {
            _activeStroke.Stroke = CreateBrush(_pencilColor);
            _activeStroke.StrokeThickness = _pencilThickness;
        }
    }

    public void BringToFront()
    {
        Topmost = false;
        Topmost = true;
    }

    public void UndoLastStroke()
    {
        if (_isDrawing || DrawingCanvas.Children.Count == 0)
        {
            return;
        }

        var lastChildIndex = DrawingCanvas.Children.Count - 1;
        var lastStroke = DrawingCanvas.Children[lastChildIndex];
        DrawingCanvas.Children.RemoveAt(lastChildIndex);
        _redoStrokes.Push(lastStroke);

        if (DrawingCanvas.Children.Count == 0 && !InteractionSurface.IsHitTestVisible && IsVisible)
        {
            Hide();
        }
    }

    public void RedoLastStroke()
    {
        if (_isDrawing || _redoStrokes.Count == 0)
        {
            return;
        }

        var restoredStroke = _redoStrokes.Pop();
        DrawingCanvas.Children.Add(restoredStroke);
        PositionWindow();

        if (!IsVisible)
        {
            Show();
        }
    }

    public void SetInteractionEnabled(bool isEnabled)
    {
        CancelActiveStroke();
        InteractionSurface.Background = isEnabled ? InteractionCaptureBrush : null;
        InteractionSurface.IsHitTestVisible = isEnabled;
        InteractionSurface.Cursor = isEnabled ? Cursors.Pen : Cursors.Arrow;
        SetClickThroughEnabled(!isEnabled);

        if (isEnabled)
        {
            PositionWindow();

            if (!IsVisible)
            {
                Show();
            }

            return;
        }

        if (!HasDrawings && IsVisible)
        {
            Hide();
        }
    }

    public void PositionWindow()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left;
        Top = workArea.Top;
        Width = workArea.Width;
        Height = workArea.Height;
    }

    private void DrawingOverlayWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionWindow();
    }

    private void DrawingOverlayWindow_OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        ApplyClickThroughStyle();
    }

    private void InteractionSurface_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_activeTool != DrawingToolKind.Pencil)
        {
            return;
        }

        var startPoint = e.GetPosition(DrawingCanvas);
        _activeStroke = new Polyline
        {
            Stroke = CreateBrush(_pencilColor),
            StrokeThickness = _pencilThickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false
        };

        _activeStroke.Points.Add(startPoint);
        _activeStroke.Points.Add(startPoint);
        DrawingCanvas.Children.Add(_activeStroke);
        _isDrawing = true;
        InteractionSurface.CaptureMouse();
        e.Handled = true;
    }

    private void InteractionSurface_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing || _activeStroke is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var currentPoint = e.GetPosition(DrawingCanvas);
        var lastPoint = _activeStroke.Points[^1];

        if ((currentPoint - lastPoint).Length < 1)
        {
            return;
        }

        _activeStroke.Points.Add(currentPoint);
    }

    private void InteractionSurface_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawing || _activeStroke is null)
        {
            return;
        }

        InteractionSurface.ReleaseMouseCapture();

        if (GetPolylineLength(_activeStroke) < MinimumStrokeLength)
        {
            DrawingCanvas.Children.Remove(_activeStroke);
        }
        else
        {
            _redoStrokes.Clear();
        }

        _activeStroke = null;
        _isDrawing = false;
        e.Handled = true;
    }

    private void CancelActiveStroke()
    {
        if (_activeStroke is not null)
        {
            DrawingCanvas.Children.Remove(_activeStroke);
            _activeStroke = null;
        }

        _isDrawing = false;
        InteractionSurface.ReleaseMouseCapture();
    }

    private void SetClickThroughEnabled(bool isEnabled)
    {
        if (_isClickThroughEnabled == isEnabled)
        {
            return;
        }

        _isClickThroughEnabled = isEnabled;
        ApplyClickThroughStyle();
    }

    private void ApplyClickThroughStyle()
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return;
        }

        var extendedStyle = GetWindowLong(_windowHandle, GwlExStyle);
        var updatedStyle = _isClickThroughEnabled
            ? extendedStyle | WsExTransparent
            : extendedStyle & ~WsExTransparent;

        if (updatedStyle != extendedStyle)
        {
            SetWindowLong(_windowHandle, GwlExStyle, updatedStyle);
        }
    }

    private static double GetPolylineLength(Polyline polyline)
    {
        if (polyline.Points.Count < 2)
        {
            return 0;
        }

        var totalLength = 0d;

        for (var index = 1; index < polyline.Points.Count; index++)
        {
            var previousPoint = polyline.Points[index - 1];
            var currentPoint = polyline.Points[index];
            totalLength += (currentPoint - previousPoint).Length;
        }

        return totalLength;
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
