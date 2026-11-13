using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using ZeroDataPeek.Models;
using ZeroDataPeek.Services;
using ZeroDataPeek.Views;

namespace ZeroDataPeek;

/// <summary>Dosya karşılaştırma ve dosyalarda arama sekmeleri.</summary>
public sealed partial class MainWindow
{
    // ================================================================== karşılaştırma

    /// <summary>İki metni seçtirir ve fark görünümünü yeni sekmede açar. <paramref name="preset"/> verilirse sol taraf o sekmedir.</summary>
    private async Task CompareAsync(DocumentTab? preset)
    {
        try
        {
            var textTabs = _tabs.Where(t => t.Editor is not null).ToList();
            var names = textTabs.Select(t => t.Title).ToList();
            names.Add(Loc.T("Dosya seç…"));
            int pick = names.Count - 1;

            int left = preset is not null ? textTabs.IndexOf(preset) : _active is not null ? textTabs.IndexOf(_active) : -1;
            if (left < 0) left = textTabs.Count > 0 ? 0 : pick;
            int right = Enumerable.Range(0, textTabs.Count).FirstOrDefault(i => i != left, pick);
            // Bölünmüş görünümde iki metin sekmesi yan yanaysa onları öner.
            if (preset is null && IsSplit && _main!.Editor is not null && _side!.Editor is not null)
            {
                left = textTabs.IndexOf(_main);
                right = textTabs.IndexOf(_side);
            }

            var choice = await Dialogs.CompareAsync(Root, Theme, names, left, right);
            if (choice is null) return;
            var first = await ResolveCompareSideAsync(textTabs, choice.Value.Left);
            if (first is null) return;
            var second = await ResolveCompareSideAsync(textTabs, choice.Value.Right);
            if (second is null) return;

            var view = new DiffDocumentView(_editorShortcuts);
            view.ApplySettings(_settings);
            view.ShortcutPressed += (_, id) => Run(id);
            view.SetContent(first.Value.Text, first.Value.Name, second.Value.Text, second.Value.Name);
            var tab = new DocumentTab(first.Value.Name + " ↔ " + second.Value.Name, null, view);
            view.StatusChanged += (_, _) =>
            {
                if (tab == _active) UpdateStatus();
            };
            AddTab(tab);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Karşılaştırma", ex);
        }
    }

    private async Task<(string Text, string Name)?> ResolveCompareSideAsync(List<DocumentTab> textTabs, int index)
    {
        if (index >= 0 && index < textTabs.Count)
        {
            var tab = textTabs[index];
            return (await tab.Editor!.GetTextAsync(), tab.Title); // kaydedilmemiş değişiklikler dahil
        }

        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        picker.FileTypeFilter.Add("*");
        var file = await picker.PickSingleFileAsync();
        if (file is null || string.IsNullOrEmpty(file.Path)) return null;
        string path = file.Path;
        try
        {
            if (new FileInfo(path).Length > TextFileIO.MaxTextBytes)
            {
                Notify(Loc.F("Karşılaştırmak için çok büyük: {0}", Path.GetFileName(path)), NoticeKind.Warning);
                return null;
            }
            var loaded = await Task.Run(() => TextFileIO.Load(path));
            if (loaded is null)
            {
                Notify(Loc.F("Metin dosyası değil, karşılaştırılamıyor: {0}", Path.GetFileName(path)), NoticeKind.Warning);
                return null;
            }
            return (loaded.Text, Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notify(Loc.F("Dosya açılamadı: {0}", Path.GetFileName(path)), NoticeKind.Error);
            return null;
        }
    }

    // ================================================================== dosyalarda arama

    /// <summary>"Dosyalarda ara" sekmesini açar (zaten açıksa ona geçer).</summary>
    private void ShowSearch()
    {
        var tab = _tabs.FirstOrDefault(t => t.View is SearchView);
        if (tab is null)
        {
            var view = new SearchView((DataTemplate)RootGrid.Resources["SearchHitTemplate"], PickFolderPathAsync);
            view.OpenRequested += (_, hit) => _ = OpenSearchHitAsync(hit);
            tab = new DocumentTab(Loc.T("Dosyalarda ara"), null, view);
            view.Folder = DefaultSearchFolder();
            AddTab(tab);
        }
        else
        {
            if (tab.View is SearchView { Folder.Length: 0 } empty) empty.Folder = DefaultSearchFolder();
            Activate(tab);
        }
        (tab.View as SearchView)?.FocusQuery();
    }

    /// <summary>Gezginde açık klasör; yoksa etkin dosyanın klasörü.</summary>
    private string DefaultSearchFolder()
    {
        if (_explorerRoot is not null && Directory.Exists(_explorerRoot)) return _explorerRoot;
        try
        {
            string? path = _tabs.LastOrDefault(t => t.FilePath is not null && t == _active)?.FilePath ?? _tabs.LastOrDefault(t => t.FilePath is not null)?.FilePath;
            string? folder = path is null ? null : Path.GetDirectoryName(path);
            return folder is not null && Directory.Exists(folder) ? folder : string.Empty;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    private async Task<string?> PickFolderPathAsync()
    {
        try
        {
            var picker = new FolderPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
            picker.FileTypeFilter.Add("*");
            var folder = await picker.PickSingleFolderAsync();
            return string.IsNullOrEmpty(folder?.Path) ? null : folder.Path;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Klasör seçici", ex);
            return null;
        }
    }

    /// <summary>Arama sonucundaki dosyayı metin olarak açar ve eşleşmeyi seçer.</summary>
    private async Task OpenSearchHitAsync(SearchHit hit)
    {
        await OpenFilesAsync(new[] { hit.Path }, asText: true);
        if (hit.IsFile) return;
        var tab = _tabs.FirstOrDefault(t => string.Equals(t.FilePath, hit.Path, StringComparison.OrdinalIgnoreCase));
        tab?.Editor?.Reveal(hit.Line, hit.Column, hit.Length);
    }
}
