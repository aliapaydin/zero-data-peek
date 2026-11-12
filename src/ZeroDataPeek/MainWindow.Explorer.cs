using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using ZeroDataPeek.Models;
using ZeroDataPeek.Services;

namespace ZeroDataPeek;

/// <summary>Ana pencerenin dosya gezgini paneli: seçilen kök klasörün içeriği, tembel açılan girintili liste.</summary>
public sealed partial class MainWindow
{
    private const int MaxFolderEntries = 2000;

    private readonly ObservableCollection<ExplorerItem> _explorerItems = new();
    private string? _explorerRoot;
    private int _explorerGeneration;

    private void InitializeExplorer()
    {
        ExplorerList.ItemsSource = _explorerItems;
        ExplorerSplitter.Dragged += (_, dx) => ExplorerColumn.Width = new GridLength(Math.Clamp(ExplorerColumn.Width.Value + dx, 170, 560));
        ExplorerSplitter.DragCompleted += (_, _) => _settings.ExplorerWidth = ExplorerColumn.Width.Value;
        ExplorerSplitter.ResetRequested += (_, _) => ExplorerColumn.Width = new GridLength(260);

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
        if (_settings.ShowExplorer && _explorerRoot is null && _active?.FilePath is { } path) RevealInExplorer(path);
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

    /// <summary>Gezgini verilen dosyanın klasörüne götürür (kök henüz seçilmediyse ya da dosya kökün dışındaysa).</summary>
    private void RevealInExplorer(string filePath)
    {
        try
        {
            string? folder = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            if (_explorerRoot is not null && filePath.StartsWith(_explorerRoot.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return;
            _ = SetExplorerRootAsync(folder);
        }
        catch (Exception)
        {
            // geçersiz yol: gezgin olduğu yerde kalır
        }
    }

    private async Task SetExplorerRootAsync(string folder)
    {
        int generation = ++_explorerGeneration;
        _explorerRoot = folder;
        _settings.ExplorerRoot = folder;
        UpdateExplorerHeader();

        var entries = await Task.Run(() => ListFolder(folder, 0));
        if (generation != _explorerGeneration) return;
        _explorerItems.Clear();
        foreach (var entry in entries) _explorerItems.Add(entry);
        UpdateExplorerHeader();
    }

    private void UpdateExplorerHeader()
    {
        bool hasRoot = _explorerRoot is not null;
        ExplorerTitle.Text = hasRoot ? new ExplorerItem(_explorerRoot!, true, 0).Name : Loc.T("Gezgin");
        ToolTipService.SetToolTip(ExplorerTitle, _explorerRoot ?? string.Empty);
        ExplorerEmpty.Visibility = hasRoot ? Visibility.Collapsed : Visibility.Visible;
        ExplorerList.Visibility = hasRoot ? Visibility.Visible : Visibility.Collapsed;
        ExplorerUpButton.IsEnabled = hasRoot && Path.GetDirectoryName(_explorerRoot!.TrimEnd('\\')) is { Length: > 0 };
        ExplorerRefreshButton.IsEnabled = hasRoot;
    }

    /// <summary>Klasörün içeriği: önce klasörler, sonra dosyalar; gizli ve sistem öğeleri atlanır.</summary>
    private static List<ExplorerItem> ListFolder(string folder, int depth)
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
                if (++count > MaxFolderEntries) break;
                bool isFolder = (entry.Attributes & FileAttributes.Directory) != 0;
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
        var children = await Task.Run(() => ListFolder(folder.FullPath, folder.Depth + 1));
        index = _explorerItems.IndexOf(folder);
        if (generation != _explorerGeneration || index < 0 || folder.IsExpanded) return;
        folder.IsExpanded = true;
        for (int i = 0; i < children.Count; i++) _explorerItems.Insert(index + 1 + i, children[i]);
    }

    private void ExplorerList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ExplorerItem item) return;
        if (item.IsFolder) _ = ToggleFolderAsync(item);
        else if (item.CanOpen) _ = OpenFilesAsync(new[] { item.FullPath });
    }

    private void ExplorerUp_Click(object sender, RoutedEventArgs e)
    {
        if (_explorerRoot is null) return;
        string? parent = Path.GetDirectoryName(_explorerRoot.TrimEnd('\\'));
        if (!string.IsNullOrEmpty(parent)) _ = SetExplorerRootAsync(parent);
    }

    private void ExplorerRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_explorerRoot is not null) _ = SetExplorerRootAsync(_explorerRoot);
    }
}
