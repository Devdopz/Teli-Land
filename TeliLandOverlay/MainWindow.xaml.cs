using System.Windows;
using System.Windows.Input;

namespace TeliLandOverlay;

public partial class MainWindow : Window
{
    private const double ScreenMargin = 24;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_OnLoaded;
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        Left = Math.Max(workArea.Left + ScreenMargin, workArea.Right - Width - ScreenMargin);
        Top = Math.Max(workArea.Top + ScreenMargin, workArea.Bottom - Height - ScreenMargin);
    }

    private void Badge_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        DragMove();
    }

    private void ExitOverlay_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
