using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace TeliLandOverlay;

public partial class ScreenViewerWindow : Window
{
    public ScreenViewerWindow()
    {
        InitializeComponent();
    }

    public void SetPeerName(string peerName)
    {
        ViewerTitleText.Text = $"{peerName}'s Screen";
    }

    public void SetStatus(string status)
    {
        ViewerStatusText.Text = status;
    }

    public void SetConnectedBadge(string badgeText, string tooltip)
    {
        ConnectionBadgeText.Text = badgeText;
        ConnectionBadgeText.ToolTip = tooltip;
    }

    public void ShowFrame(byte[] frameBytes)
    {
        var bitmap = CreateBitmap(frameBytes);
        ViewerImage.Source = bitmap;
        PlaceholderOverlay.Visibility = Visibility.Collapsed;
        ViewerStatusText.Text = "Live sharing in progress";
    }

    public void ResetToWaiting()
    {
        ViewerImage.Source = null;
        PlaceholderOverlay.Visibility = Visibility.Visible;
    }

    private static BitmapImage CreateBitmap(byte[] frameBytes)
    {
        using var memoryStream = new MemoryStream(frameBytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = memoryStream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
