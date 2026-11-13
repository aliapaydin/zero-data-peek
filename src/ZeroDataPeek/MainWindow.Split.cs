using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZeroDataPeek.Models;
using ZeroDataPeek.Services;

namespace ZeroDataPeek;

/// <summary>
/// Bölünmüş görünüm: iki sekme yan yana. Sol (ana) bölmede <see cref="_main"/>, sağ bölmede <see cref="_side"/> durur;
/// <see cref="_active"/> odaktaki sekmedir ve menüler, kısayollar, durum çubuğu ona göre çalışır.
/// Sağ bölmedeki sekme sabittir; diğer sekmelere tıklandıkça sol bölme değişir.
/// </summary>
public sealed partial class MainWindow
{
    private DocumentTab? _main;
    private DocumentTab? _side;
    private double _splitRatio = 0.5;

    private bool IsSplit => _side is not null && _main is not null;

    private void InitializeSplit()
    {
        SplitSplitter.Dragged += (_, dx) =>
        {
            double total = DocumentHost.ActualWidth;
            if (!IsSplit || total < 240) return;
            _splitRatio = Math.Clamp((MainPaneColumn.ActualWidth + dx) / total, 0.15, 0.85);
            ApplySplitWidths();
        };
        SplitSplitter.ResetRequested += (_, _) =>
        {
            _splitRatio = 0.5;
            ApplySplitWidths();
        };
        MainHeader.Tapped += (_, _) => { if (_main is not null) Activate(_main); };
        SideHeader.Tapped += (_, _) => { if (_side is not null) Activate(_side); };
    }

    private void ApplySplitWidths()
    {
        bool split = IsSplit;
        MainPaneColumn.Width = new GridLength(split ? _splitRatio : 1, GridUnitType.Star);
        SidePaneColumn.Width = split ? new GridLength(1 - _splitRatio, GridUnitType.Star) : new GridLength(0);
    }

    /// <summary>Hangi görünümün hangi bölmede görüneceğini uygular.</summary>
    private void LayoutPanes()
    {
        if (_side is not null && (_main is null || _side == _main || !_tabs.Contains(_side))) _side = null;
        bool split = IsSplit;
        foreach (var tab in _tabs)
        {
            bool side = split && tab == _side;
            bool shown = side || tab == _main;
            Grid.SetColumn(tab.View, side ? 2 : 0);
            Grid.SetColumnSpan(tab.View, split ? 1 : 3);
            var visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            if (tab.View.Visibility != visibility) tab.View.Visibility = visibility;
        }
        ApplySplitWidths();

        var paneVisibility = split ? Visibility.Visible : Visibility.Collapsed;
        SplitSplitter.Visibility = paneVisibility;
        MainHeader.Visibility = paneVisibility;
        SideHeader.Visibility = paneVisibility;
        MenuSplit.IsChecked = split;
        if (!split) return;

        MainHeaderText.Text = _main!.DisplayTitle;
        SideHeaderText.Text = _side!.DisplayTitle;
        bool sideActive = _active == _side;
        MainHeader.Opacity = sideActive ? 0.55 : 1;
        SideHeader.Opacity = sideActive ? 1 : 0.55;
        MainHeaderMark.Visibility = sideActive ? Visibility.Collapsed : Visibility.Visible;
        SideHeaderMark.Visibility = sideActive ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Bölmelerden birinin içine tıklandı: o sekme etkin olur (görünümler yer değiştirmez).</summary>
    private void PaneGotFocus(DocumentTab tab)
    {
        if (!IsSplit || _active == tab || (tab != _main && tab != _side) || !_tabs.Contains(tab)) return;
        _active = tab;
        LayoutPanes();
        SyncSelection();
        UpdateUi();
        MarkActiveInExplorer();
    }

    private void ToggleSplit()
    {
        if (_side is not null)
        {
            CloseSplit();
            return;
        }
        if (_active is null || _tabs.Count < 2)
        {
            Notify(Loc.T("Bölünmüş görünüm için en az iki sekme açık olmalı"), NoticeKind.Info);
            return;
        }
        OpenInSidePane(_active);
    }

    /// <summary>Sekmeyi sağ bölmeye sabitler; sol bölmede bir önceki sekme kalır.</summary>
    private void OpenInSidePane(DocumentTab tab)
    {
        if (!_tabs.Contains(tab)) return;
        if (_tabs.Count < 2)
        {
            Notify(Loc.T("Bölünmüş görünüm için en az iki sekme açık olmalı"), NoticeKind.Info);
            return;
        }
        if (_main is null || _main == tab)
        {
            int index = _tabs.IndexOf(tab);
            _main = _tabs[index > 0 ? index - 1 : 1];
        }
        _side = tab;
        Activate(tab);
    }

    private void CloseSplit()
    {
        if (_side is null) return;
        var keep = _active ?? _main;
        _side = null;
        if (keep is not null) Activate(keep);
        else LayoutPanes();
    }

    private void SideClose_Click(object sender, RoutedEventArgs e) => CloseSplit();

    /// <summary>İki bölmenin içeriğini değiştirir.</summary>
    private void SwapPanes()
    {
        if (!IsSplit) return;
        (_main, _side) = (_side, _main);
        LayoutPanes();
        UpdateUi();
    }
}
