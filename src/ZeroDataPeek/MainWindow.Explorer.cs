using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using ZeroDataPeek.Models;
using ZeroDataPeek.Services;
using ZeroDataPeek.Views;

namespace ZeroDataPeek;

/// <summary>
/// Ana pencerenin dosya gezgini paneli: seçilen kök klasörün içeriği, tembel açılan girintili liste.
/// Başlıktaki klasör adı sürücüleri ve sık kullanılan yerleri açar; satırlara sağ tıklayınca dosya işlemleri gelir.
/// </summary>
public sealed partial class MainWindow
{
    private const int MaxFolderEntries = 2000;

    private readonly ObservableCollection<ExplorerItem> _explorerItems = new();
    private string? _explorerRoot;
    private string _explorerFilter = string.Empty;
    private int _explorerGeneration;

    private void InitializeExplorer()
    {
        ExplorerList.ItemsSource = _explorerItems;
        ExplorerSplitter.Dragged += (_, dx) => ExplorerColumn.Width = new GridLength(Math.Clamp(ExplorerColumn.Width.Value + dx, 170, 560));
        ExplorerSplitter.DragCompleted += (_, _) => _settings.ExplorerWidth = ExplorerColumn.Width.Value;
        ExplorerSplitter.ResetRequested += (_, _) => ExplorerColumn.Width = new GridLength(260);

        var places = new MenuFlyout();
        places.Opening += (_, _) => FillPlaces(places);
        ExplorerTitleButton.Flyout = places;

        string saved = _settings.ExplorerRoot;
        if (saved.Length > 0 && Directory.Exists(saved)) _ = SetExplorerRootAsync(saved);
        else UpdateExplorerHeader();
    }

    private void ApplyExplorerLayout()
    {
        bool show = _settings.ShowExplorer;
        ExplorerPane.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ExplorerSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ExplorerColumn.Width = new GridLength(show ? Math.Clamp(_settings.ExplorerWidth, 170, 560) : 0);
        MenuExplorer.IsChecked = show;
    }

    private void ToggleExplorer()
    {
        _settings.ShowExplorer = !_settings.ShowExplorer;
        ApplyExplorerLayout();
        if (_settings.ShowExplorer && _explorerRoot is null && _active?.FilePath is { } path) RevealInExplorer(path, force: false);
    }

    // ================================================================== sürücüler ve yerler

    /// <summary>Başlıktaki açılır menü: sık kullanılan klasörler ve sürücüler.</summary>
    private void FillPlaces(MenuFlyout menu)
    {
        menu.Items.Clear();

        void Place(string label, string glyph, string? path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            var item = new MenuFlyoutItem { Text = label, Icon = new FontIcon { Glyph = glyph } };
            ToolTipService.SetToolTip(item, path);
            item.Click += (_, _) => _ = SetExplorerRootAsync(path);
            menu.Items.Add(item);
        }

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Place(Loc.T("Masaüstü"), "", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Place(Loc.T("Belgeler"), "", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        Place(Loc.T("İndirilenler"), "", profile.Length > 0 ? Path.Combine(profile, "Downloads") : null);
        Place(Loc.T("Kullanıcı klasörü"), "", profile);
        if (_active?.FilePath is { } activePath) Place(Loc.T("Etkin dosyanın klasörü"), "", Path.GetDirectoryName(activePath));

        menu.Items.Add(new MenuFlyoutSeparator());
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                string label = drive.Name.TrimEnd('\\');
                try
                {
                    if (!drive.IsReady) continue;
                    if (!string.IsNullOrWhiteSpace(drive.VolumeLabel)) label += "  " + drive.VolumeLabel;
                    label += "  ·  " + Loc.F("{0} boş", FormatSize(drive.AvailableFreeSpace));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // etiketi ya da boş alanı okunamayan sürücü: yalnızca harfiyle gösterilir
                }
                string glyph = drive.DriveType switch
                {
                    DriveType.Removable => "",
                    DriveType.Network => "",
                    DriveType.CDRom => "",
                    _ => "",
                };
                Place(label, glyph, drive.RootDirectory.FullName);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("Sürücüler", ex);
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        var browse = new MenuFlyoutItem { Text = Loc.T("Klasör aç…"), Icon = new FontIcon { Glyph = "" } };
        browse.Click += (_, _) => _ = PickExplorerFolderAsync();
        menu.Items.Add(browse);
    }

    private static string FormatSize(long bytes)
    {
        double value = bytes;
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return value.ToString(value >= 100 || unit == 0 ? "0" : "0.#", System.Globalization.CultureInfo.CurrentCulture) + " " + units[unit];
    }

    private async Task PickExplorerFolderAsync()
    {
        try
        {
            var picker = new FolderPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
            picker.FileTypeFilter.Add("*");
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null || string.IsNullOrEmpty(folder.Path)) return;
            if (!_settings.ShowExplorer) ToggleExplorer();
            await SetExplorerRootAsync(folder.Path);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Klasör seçici", ex);
        }
    }

    /// <summary>Gezgini verilen dosyanın klasörüne götürür. force değilse, dosya zaten kökün altındaysa kök değişmez.</summary>
    private void RevealInExplorer(string filePath, bool force)
    {
        try
        {
            string? folder = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            bool inside = _explorerRoot is not null && filePath.StartsWith(_explorerRoot.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
            if (inside && !force) return;
            if (!_settings.ShowExplorer) ToggleExplorer();
            _ = SetExplorerRootAsync(folder);
        }
        catch (Exception)
        {
            // geçersiz yol: gezgin olduğu yerde kalır
        }
    }

    // ================================================================== listeleme

    private async Task SetExplorerRootAsync(string folder)
    {
        int generation = ++_explorerGeneration;
        _explorerRoot = folder;
        _settings.ExplorerRoot = folder;
        UpdateExplorerHeader();

        string filter = _explorerFilter;
        var entries = await Task.Run(() => ListFolder(folder, 0, filter));
        if (generation != _explorerGeneration) return;
        _explorerItems.Clear();
        foreach (var entry in entries) _explorerItems.Add(entry);
        UpdateExplorerHeader();
        MarkActiveInExplorer();
    }

    /// <summary>Listeyi yeniden okur; açık olan klasörler açık kalır (dosya işlemlerinden ve süzgeç değişiminden sonra).</summary>
    private async Task RefreshExplorerAsync()
    {
        if (_explorerRoot is null) return;
        var expanded = _explorerItems.Where(i => i.IsFolder && i.IsExpanded).Select(i => i.FullPath).ToList();
        await SetExplorerRootAsync(_explorerRoot);
        foreach (var path in expanded)
        {
            var folder = _explorerItems.FirstOrDefault(i => i.IsFolder && string.Equals(i.FullPath, path, StringComparison.OrdinalIgnoreCase));
            if (folder is not null && !folder.IsExpanded) await ToggleFolderAsync(folder);
        }
    }

    private void UpdateExplorerHeader()
    {
        bool hasRoot = _explorerRoot is not null;
        ExplorerTitle.Text = hasRoot ? new ExplorerItem(_explorerRoot!, true, 0).Name : Loc.T("Gezgin");
        ToolTipService.SetToolTip(ExplorerTitleButton, hasRoot ? _explorerRoot + "\n" + Loc.T("Sürücü ya da klasör değiştir") : Loc.T("Sürücü ya da klasör seç"));
        ExplorerEmpty.Visibility = hasRoot ? Visibility.Collapsed : Visibility.Visible;
        ExplorerList.Visibility = hasRoot ? Visibility.Visible : Visibility.Collapsed;
        ExplorerFilterBox.Visibility = hasRoot ? Visibility.Visible : Visibility.Collapsed;
        ExplorerUpButton.IsEnabled = hasRoot && Path.GetDirectoryName(_explorerRoot!.TrimEnd('\\')) is { Length: > 0 };
        ExplorerRefreshButton.IsEnabled = hasRoot;
    }

    /// <summary>Klasörün içeriği: önce klasörler, sonra dosyalar; gizli ve sistem öğeleri atlanır. Süzgeç yalnızca dosya adlarına uygulanır.</summary>
    private static List<ExplorerItem> ListFolder(string folder, int depth, string filter)
    {
        var folders = new List<ExplorerItem>();
        var files = new List<ExplorerItem>();
        try
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
                RecurseSubdirectories = false,
            };
            int count = 0;
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", options))
            {
                bool isFolder = (entry.Attributes & FileAttributes.Directory) != 0;
                if (!isFolder && filter.Length > 0 && entry.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                if (++count > MaxFolderEntries) break;
                (isFolder ? folders : files).Add(new ExplorerItem(entry.FullName, isFolder, depth));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            // erişilemeyen klasör boş görünür
        }

        var comparer = StringComparer.CurrentCultureIgnoreCase;
        folders.Sort((a, b) => comparer.Compare(a.Name, b.Name));
        files.Sort((a, b) => comparer.Compare(a.Name, b.Name));
        folders.AddRange(files);
        return folders;
    }

    private async Task ToggleFolderAsync(ExplorerItem folder)
    {
        int index = _explorerItems.IndexOf(folder);
        if (index < 0) return;

        if (folder.IsExpanded)
        {
            folder.IsExpanded = false;
            while (index + 1 < _explorerItems.Count && _explorerItems[index + 1].Depth > folder.Depth)
            {
                _explorerItems.RemoveAt(index + 1);
            }
            return;
        }

        int generation = _explorerGeneration;
        string filter = _explorerFilter;
        var children = await Task.Run(() => ListFolder(folder.FullPath, folder.Depth + 1, filter));
        index = _explorerItems.IndexOf(folder);
        if (generation != _explorerGeneration || index < 0 || folder.IsExpanded) return;
        folder.IsExpanded = true;
        for (int i = 0; i < children.Count; i++) _explorerItems.Insert(index + 1 + i, children[i]);
        MarkActiveInExplorer();
    }

    /// <summary>Etkin sekmenin dosyasını ağaçta kalın gösterir.</summary>
    private void MarkActiveInExplorer()
    {
        string? active = _active?.FilePath;
        foreach (var item in _explorerItems)
        {
            item.IsActive = !item.IsFolder && active is not null && string.Equals(item.FullPath, active, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ================================================================== tıklama ve düğmeler

    private void ExplorerList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ExplorerItem item) return;
        if (item.IsFolder) _ = ToggleFolderAsync(item);
        else if (item.CanOpen) _ = OpenFilesAsync(new[] { item.FullPath });
        else Notify(Loc.F("Bu dosya türü açılamıyor: {0}", item.Name), NoticeKind.Warning);
    }

    private void ExplorerUp_Click(object sender, RoutedEventArgs e)
    {
        if (_explorerRoot is null) return;
        string? parent = Path.GetDirectoryName(_explorerRoot.TrimEnd('\\'));
        if (!string.IsNullOrEmpty(parent)) _ = SetExplorerRootAsync(parent);
    }

    private void ExplorerRefresh_Click(object sender, RoutedEventArgs e) => _ = RefreshExplorerAsync();

    private void ExplorerFilter_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            ExplorerFilterBox.Text = string.Empty;
        }
        else if (e.Key != Windows.System.VirtualKey.Enter)
        {
            return;
        }
        e.Handled = true;
        string filter = ExplorerFilterBox.Text.Trim();
        if (filter == _explorerFilter) return;
        _explorerFilter = filter;
        _ = RefreshExplorerAsync();
    }

    // ================================================================== sağ tık menüsü

    private void ExplorerList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (_explorerRoot is null) return;
        var item = (e.OriginalSource as FrameworkElement)?.DataContext as ExplorerItem;
        var menu = new MenuFlyout();

        void Add(string text, string glyph, Action action, bool enabled = true)
        {
            var entry = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph }, IsEnabled = enabled };
            entry.Click += (_, _) => action();
            menu.Items.Add(entry);
        }

        // Yeni öğeler: klasöre tıklandıysa onun içine, dosyaya tıklandıysa yanına, boşluğa tıklandıysa köke.
        string targetFolder = item is null ? _explorerRoot : item.IsFolder ? item.FullPath : Path.GetDirectoryName(item.FullPath) ?? _explorerRoot;

        if (item is not null && !item.IsFolder)
        {
            Add(Loc.T("Aç"), "", () => _ = OpenFilesAsync(new[] { item.FullPath }), item.CanOpen);
            if (DataEngine.Supports(item.FullPath) && !FileKinds.IsBinaryFormat(item.FullPath))
            {
                Add(Loc.T("Metin olarak aç"), "", () => _ = OpenFilesAsync(new[] { item.FullPath }, asText: true));
            }
            menu.Items.Add(new MenuFlyoutSeparator());
        }
        else if (item is not null)
        {
            Add(Loc.T("Bu klasörü kök yap"), "", () => _ = SetExplorerRootAsync(item.FullPath));
            menu.Items.Add(new MenuFlyoutSeparator());
        }

        Add(Loc.T("Yeni dosya…"), "", () => _ = CreateInExplorerAsync(targetFolder, folder: false));
        Add(Loc.T("Yeni klasör…"), "", () => _ = CreateInExplorerAsync(targetFolder, folder: true));

        if (item is not null)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            Add(Loc.T("Dosya Gezgini'nde göster"), "", () => RevealPath(item.FullPath));
            Add(Loc.T("Yolu kopyala"), "", () => CopyText(item.FullPath, Loc.T("Yol kopyalandı")));
            menu.Items.Add(new MenuFlyoutSeparator());
            Add(Loc.T("Yeniden adlandır…"), "", () => _ = RenameInExplorerAsync(item));
            Add(Loc.T("Sil"), "", () => _ = DeleteInExplorerAsync(item));
        }

        menu.ShowAt(ExplorerList, new FlyoutShowOptions { Position = e.GetPosition(ExplorerList) });
        e.Handled = true;
    }

    private static bool IsValidName(string name) =>
        name.Length > 0 && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name.Trim('.', ' ').Length > 0;

    private async Task CreateInExplorerAsync(string parent, bool folder)
    {
        string? name = await Dialogs.PromptAsync(Root, Theme, folder ? Loc.T("Yeni klasör") : Loc.T("Yeni dosya"),
            folder ? Loc.T("Yeni klasör") : Loc.T("yeni.txt"), Loc.T("Oluştur"));
        if (name is null) return;
        if (!IsValidName(name))
        {
            Notify(Loc.T("Bu ad kullanılamaz"), NoticeKind.Warning);
            return;
        }

        string path = Path.Combine(parent, name);
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                Notify(Loc.F("Bu adda bir öğe zaten var: {0}", name), NoticeKind.Warning);
                return;
            }
            if (folder) Directory.CreateDirectory(path);
            else File.WriteAllBytes(path, Array.Empty<byte>());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            await Dialogs.MessageAsync(Root, Theme, Loc.T("Oluşturulamadı"), path + "\n\n" + ex.Message);
            return;
        }

        await RefreshExplorerAsync();
        Notify(Loc.F("Oluşturuldu: {0}", name));
        if (!folder) await OpenFilesAsync(new[] { path });
    }

    private async Task RenameInExplorerAsync(ExplorerItem item)
    {
        string? name = await Dialogs.PromptAsync(Root, Theme, Loc.T("Yeniden adlandır"), item.Name, Loc.T("Yeniden adlandır"));
        if (name is null || name == item.Name) return;
        if (!IsValidName(name))
        {
            Notify(Loc.T("Bu ad kullanılamaz"), NoticeKind.Warning);
            return;
        }

        string target = Path.Combine(Path.GetDirectoryName(item.FullPath) ?? string.Empty, name);
        try
        {
            if (File.Exists(target) || Directory.Exists(target))
            {
                // Yalnızca harf büyüklüğü değişiyorsa aynı öğedir; değilse çakışma var.
                if (!string.Equals(target, item.FullPath, StringComparison.OrdinalIgnoreCase))
                {
                    Notify(Loc.F("Bu adda bir öğe zaten var: {0}", name), NoticeKind.Warning);
                    return;
                }
            }
            if (item.IsFolder) Directory.Move(item.FullPath, target);
            else File.Move(item.FullPath, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            await Dialogs.MessageAsync(Root, Theme, Loc.T("Yeniden adlandırılamadı"), item.FullPath + "\n\n" + ex.Message);
            return;
        }

        // Açık sekmeler yeni yolu izlesin (klasör yeniden adlandırıldıysa içindeki dosyalar da).
        string oldPrefix = item.FullPath.TrimEnd('\\') + "\\";
        foreach (var tab in _tabs)
        {
            if (tab.FilePath is not { } path) continue;
            if (!item.IsFolder && string.Equals(path, item.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                tab.FilePath = target;
                tab.Editor?.SetFileName(tab.Title);
                tab.TouchDiskStamp();
            }
            else if (item.IsFolder && path.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
            {
                tab.FilePath = Path.Combine(target, path[oldPrefix.Length..]);
                tab.TouchDiskStamp();
            }
        }

        await RefreshExplorerAsync();
        UpdateUi();
        Notify(Loc.F("Yeniden adlandırıldı: {0}", name));
    }

    private async Task DeleteInExplorerAsync(ExplorerItem item)
    {
        bool confirmed = await Dialogs.ConfirmAsync(Root, Theme, Loc.T("Geri Dönüşüm Kutusu'na gönder"),
            item.IsFolder
                ? Loc.F("\"{0}\" klasörü içindekilerle birlikte Geri Dönüşüm Kutusu'na gönderilsin mi?", item.Name)
                : Loc.F("\"{0}\" Geri Dönüşüm Kutusu'na gönderilsin mi?", item.Name),
            Loc.T("Gönder"));
        if (!confirmed) return;

        try
        {
            string path = item.FullPath;
            await Task.Run(() =>
            {
                if (item.IsFolder)
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                else
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            await Dialogs.MessageAsync(Root, Theme, Loc.T("Silinemedi"), item.FullPath + "\n\n" + ex.Message);
            return;
        }

        await RefreshExplorerAsync();
        Notify(Loc.F("Geri Dönüşüm Kutusu'na gönderildi: {0}", item.Name));
    }

    // ================================================================== ortak küçük eylemler

    private void RevealPath(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return;
            Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            CrashLog.Write("Dosya Gezgini", ex);
        }
    }

    private void CopyText(string text, string notice)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            Notify(notice);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Pano", ex);
        }
    }
}
