using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZeroDataPeek.Services;

namespace ZeroDataPeek;

/// <summary>Kompakt mod: pencerenin kendi boşluklarını ve liste satırlarını yoğunluğa göre ayarlar (bkz. Services/Density).</summary>
public sealed partial class MainWindow
{
    private void ToggleCompact()
    {
        _settings.CompactMode = !_settings.CompactMode;
        ApplySettings();
        Notify(_settings.CompactMode ? Loc.T("Kompakt mod açık") : Loc.T("Kompakt mod kapalı"), NoticeKind.Info);
    }

    private void ApplyDensity()
    {
        bool changed = Density.Compact != _settings.CompactMode;
        Density.Compact = _settings.CompactMode;

        RootGrid.RowDefinitions[0].Height = new GridLength(Density.TitleBarHeight);
        MainMenu.Height = Density.MenuHeight;
        ContentGrid.Padding = new Thickness(Density.ContentPadding);
        StatusBar.Padding = Density.Compact ? new Thickness(10, 0, 6, 1) : new Thickness(14, 2, 8, 4);
        var radius = new CornerRadius(Density.CardRadius);
        ExplorerPane.CornerRadius = radius;
        TabPane.CornerRadius = radius;
        DocumentCard.CornerRadius = radius;

        // Liste satırları varsayılan olarak en az 40 px'tir; yoğunluğa göre küçült.
        var itemStyle = new Style(typeof(ListViewItem));
        itemStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, Density.ListItemMinHeight));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, Density.Compact ? new Thickness(8, 0, 6, 0) : new Thickness(12, 0, 8, 0)));
        ExplorerList.ItemContainerStyle = itemStyle;
        TabList.ItemContainerStyle = itemStyle;

        MenuCompact.IsChecked = Density.Compact;
        if (!changed) return;

        // Satır yüksekliği ve yazı boyutu şablonlara bir kez bağlanır: listeleri yeniden kur.
        TabList.ItemsSource = null;
        TabList.ItemsSource = _tabs;
        SyncSelection();
        _ = RefreshExplorerAsync();
    }
}
