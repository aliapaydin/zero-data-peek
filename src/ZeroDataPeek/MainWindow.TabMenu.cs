using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using ZeroDataPeek.Models;
using ZeroDataPeek.Services;
using ZeroDataPeek.Views;

namespace ZeroDataPeek;

/// <summary>Sekmelerin sağ tık menüsü (yatay şeritte ve dikey listede aynı): kaydet, farklı kaydet, çoğalt, kapat…</summary>
public sealed partial class MainWindow
{
    private MenuFlyout CreateTabMenu(DocumentTab tab)
    {
        var menu = new MenuFlyout();
        menu.Opening += (_, _) => FillTabMenu(menu, tab);
        return menu;
    }

    private void FillTabMenu(MenuFlyout menu, DocumentTab tab)
    {
        menu.Items.Clear();

        void Item(string text, string? glyph, Action action, bool enabled = true, string? shortcut = null)
        {
            var item = new MenuFlyoutItem { Text = text, IsEnabled = enabled };
            if (glyph is not null) item.Icon = new FontIcon { Glyph = glyph };
            if (shortcut is not null) item.KeyboardAcceleratorTextOverride = shortcut;
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        bool editable = tab.Editor is not null || tab.Doc is not null;
        bool hasPath = tab.FilePath is not null;
        int index = _tabs.IndexOf(tab);

        Item(Loc.T("Kaydet"), "", () => _ = SaveFromMenuAsync(tab, saveAs: false), editable, "Ctrl+S");
        Item(Loc.T("Farklı kaydet…"), "", () => _ = SaveFromMenuAsync(tab, saveAs: true), editable || tab.Data is not null, "Ctrl+Shift+S");
        Item(Loc.T("Çoğalt"), "", () => _ = DuplicateTabAsync(tab), tab.Editor is not null);
        menu.Items.Add(new MenuFlyoutSeparator());
        Item(Loc.T("Dosya yolunu kopyala"), "", () => CopyText(tab.FilePath!, Loc.T("Yol kopyalandı")), hasPath);
        Item(Loc.T("Dosya Gezgini'nde göster"), "", () => RevealPath(tab.FilePath!), hasPath);
        Item(Loc.T("Gezgin panelinde göster"), "", () => RevealInExplorer(tab.FilePath!, force: true), hasPath);
        menu.Items.Add(new MenuFlyoutSeparator());
        Item(Loc.T("Kapat"), "", () => _ = CloseTabAsync(tab), true, "Ctrl+W");
        Item(Loc.T("Diğerlerini kapat"), null, () => _ = CloseManyAsync(_tabs.Where(t => t != tab).ToList()), _tabs.Count > 1);
        Item(Loc.T("Sağdakileri kapat"), null, () => _ = CloseManyAsync(_tabs.Skip(index + 1).ToList()), index >= 0 && index < _tabs.Count - 1);
        Item(Loc.T("Tümünü kapat"), null, () => _ = CloseManyAsync(_tabs.ToList()));
    }

    private async Task SaveFromMenuAsync(DocumentTab tab, bool saveAs)
    {
        if (tab.Data is { } data)
        {
            Activate(tab);
            await data.ExportAsync();
            return;
        }
        await SaveAsync(tab, saveAs);
    }

    /// <summary>Metin sekmesinin içeriğini yeni, kaydedilmemiş bir sekmeye kopyalar.</summary>
    private async Task DuplicateTabAsync(DocumentTab tab)
    {
        if (tab.Editor is not { } source) return;
        try
        {
            string text = await source.GetTextAsync();
            string extension = Path.GetExtension(tab.Title);
            string name = Loc.F("{0} - kopya", Path.GetFileNameWithoutExtension(tab.Title)) + extension;

            var editor = CreateEditor();
            editor.SetFileName(name);
            editor.Load(new LoadedText(text, source.Encoding, source.Eol));
            var copy = new DocumentTab(name, null, editor);
            HookEditor(copy, editor);
            AddTab(copy);
            copy.IsDirty = true; // içerik henüz hiçbir dosyada değil
            Notify(Loc.F("Çoğaltıldı: {0}", name), NoticeKind.Info);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Sekme çoğaltma", ex);
        }
    }

    /// <summary>Dikey sekme listesinde sağ tık.</summary>
    private void TabList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not DocumentTab tab) return;
        var menu = new MenuFlyout();
        FillTabMenu(menu, tab);
        menu.ShowAt(TabList, new FlyoutShowOptions { Position = e.GetPosition(TabList) });
        e.Handled = true;
    }
}
