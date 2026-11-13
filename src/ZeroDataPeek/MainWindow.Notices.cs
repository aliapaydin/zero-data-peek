using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ZeroDataPeek.Services;

namespace ZeroDataPeek;

public enum NoticeKind
{
    Success,
    Info,
    Warning,
    Error,
}

/// <summary>
/// Rahatsız etmeyen bildirimler: durum çubuğunun solunda birkaç saniye görünüp kaybolan kısa bir satır
/// ("Kaydedildi: rapor.sql"). Pencere açmaz, odağı almaz; yalnızca kullanıcının yanıt vermesi gereken
/// durumlarda (kaydedilemedi, üzerine yazılsın mı) diyalog kullanılır.
/// </summary>
public sealed partial class MainWindow
{
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _noticeTimer;

    private void Notify(string message, NoticeKind kind = NoticeKind.Success)
    {
        try
        {
            string glyph = kind switch
            {
                NoticeKind.Info => "\uE946",
                NoticeKind.Warning => "\uE7BA",
                NoticeKind.Error => "\uEA39",
                _ => "\uE73E",
            };
            Brush? brush = kind switch
            {
                NoticeKind.Warning => new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xE0, 0x9A, 0x1F)),
                NoticeKind.Error => new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xD1, 0x34, 0x38)),
                NoticeKind.Success => new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x2E, 0x9E, 0x5B)),
                _ => null,
            };
            NoticeIcon.Glyph = glyph;
            if (brush is not null) NoticeIcon.Foreground = brush;
            else NoticeIcon.ClearValue(IconElement.ForegroundProperty);
            NoticeText.Text = message;
            ToolTipService.SetToolTip(NoticePanel, message);
            NoticePanel.Visibility = Visibility.Visible;
            StatusText.Visibility = Visibility.Collapsed;

            if (_noticeTimer is null)
            {
                _noticeTimer = DispatcherQueue.CreateTimer();
                _noticeTimer.IsRepeating = false;
                _noticeTimer.Tick += (_, _) =>
                {
                    NoticePanel.Visibility = Visibility.Collapsed;
                    StatusText.Visibility = Visibility.Visible;
                    if (!_settings.ShowStatusBar) UpdateUi(); // başlığı eski haline getir
                };
            }
            _noticeTimer.Stop();
            _noticeTimer.Interval = TimeSpan.FromSeconds(kind is NoticeKind.Warning or NoticeKind.Error ? 7 : 4);
            _noticeTimer.Start();

            // Durum çubuğu gizliyse bildirim de görünmez; o durumda başlık çubuğunda kısa süre göster.
            if (!_settings.ShowStatusBar) TitleText.Text = message;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Bildirim", ex);
        }
    }
}
