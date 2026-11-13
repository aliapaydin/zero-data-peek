using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using ZeroDataPeek.Models;
using ZeroDataPeek.Services;
using ZeroDataPeek.Views;

namespace ZeroDataPeek;

/// <summary>
/// Ana pencere: sekmeler (yatay şerit ya da dikey liste), belge alanı, menü, durum çubuğu ve komut paleti.
/// Sekmelerin tek doğruluk kaynağı <see cref="_tabs"/> listesidir; iki sekme görünümü ona göre eşitlenir.
/// </summary>
public sealed partial class MainWindow : Window
{
    public const string GitHubUrl = "https://github.com/aliapaydin/zero-data-peek";

    private sealed record AppCommand(string Id, string Title, string Shortcut, Action Run, bool InPalette);

    private readonly AppSettings _settings = AppSettings.Current;
    private readonly ObservableCollection<DocumentTab> _tabs = new();
    private readonly Dictionary<DocumentTab, TabViewItem> _stripItems = new();
    private readonly Dictionary<string, AppCommand> _commands = new();
    private readonly List<AppCommand> _commandOrder = new();
    private readonly List<EditorShortcut> _editorShortcuts = new();
    private readonly IntPtr _hwnd;
    private readonly WindowPlacement _placement;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _caretTimer;
    private DocumentTab? _active;
    private bool _syncingTabs;
    private bool _stripSyncQueued;
    private bool _adoptQueued;
    private bool _closeConfirmed;
    private int _untitledCount;

    public MainWindow(IReadOnlyList<string> launchPaths)
    {
        InitializeComponent();
        Loc.Apply(this); // XAML metinleri seçili dile
        WindowHelper.Track(this);
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        ExtendsContentIntoTitleBar = true;
        AppWindow.SetIcon(WindowHelper.IconPath);
        _placement = new WindowPlacement(AppWindow, DispatcherQueue, () => _settings.MainWindowBounds, value => _settings.MainWindowBounds = value);
        _placement.Restore(new global::Windows.Graphics.SizeInt32(1280, 820));

        TabList.ItemsSource = _tabs;
        _tabs.CollectionChanged += Tabs_CollectionChanged;

        TabSplitter.Dragged += (_, dx) => TabPaneColumn.Width = new GridLength(Math.Clamp(TabPaneColumn.Width.Value + dx, 160, 520));
        TabSplitter.DragCompleted += (_, _) => _settings.TabPaneWidth = TabPaneColumn.Width.Value;
        TabSplitter.ResetRequested += (_, _) => TabPaneColumn.Width = new GridLength(240);

        _caretTimer = DispatcherQueue.CreateTimer();
        _caretTimer.Interval = TimeSpan.FromMilliseconds(60);
        _caretTimer.IsRepeating = false;
        _caretTimer.Tick += (_, _) => UpdateStatus();

        RootGrid.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        BuildCommands();
        BuildStatusFlyouts();
        InitializeExplorer();
        InitializeBackup();
        ShellMenu.Publish(_settings);
        RefreshSessions();

        AppWindow.Closing += AppWindow_Closing;
        Closed += (_, _) =>
        {
            SaveSession();
            // Saklanacak kaydedilmemiş metin sekmesi kalmadıysa eski yedekler de gitsin.
            if (!_settings.KeepUnsaved || !_tabs.Any(t => t.IsDirty && t.Editor is not null)) TabBackup.Clear();
            _placement.Save();
            DataEngine.Shared.Dispose();
        };
        RootGrid.Loaded += (_, _) =>
        {
            ApplyTabLayout(); // başlık çubuğu sürükleme alanı, şerit yüklendikten sonra kesinleşir
            WindowHelper.ApplyTitleBarColors(AppWindow, RootGrid.ActualTheme == ElementTheme.Dark);
        };

        ApplySettings();
        RefreshRecent();
        UpdateUi();
        _ = StartAsync(launchPaths);
    }

    private ElementTheme Theme => RootGrid.ActualTheme;

    private XamlRoot Root => Content.XamlRoot;

    // ================================================================== açılış ve kapanış

    private async Task StartAsync(IReadOnlyList<string> launchPaths)
    {
        try
        {
            await RestoreBackupsAsync(); // önce yedekler: oturumdaki aynı dosya ikinci kez açılmaz
            if (_settings.RestoreSession)
            {
                var session = _settings.SessionFiles.Where(File.Exists).ToList();
                string active = _settings.SessionActiveFile;
                await OpenFilesAsync(session, reportMissing: false);
                var last = _tabs.FirstOrDefault(t => string.Equals(t.FilePath, active, StringComparison.OrdinalIgnoreCase));
                if (last is not null) Activate(last);
            }
            await OpenFilesAsync(launchPaths);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Açılış", ex);
        }
    }

    /// <summary>Başka bir örnekten yönlendirilen açma isteği (bkz. Program.cs).</summary>
    public void OpenFromActivation(IReadOnlyList<string> paths)
    {
        NativeWindow.BringToFront(_hwnd);
        _ = OpenFilesAsync(paths);
    }

    private void SaveSession()
    {
        try
        {
            _settings.SessionFiles = _tabs.Where(t => t.FilePath is not null).Select(t => t.FilePath!).ToList();
            _settings.SessionActiveFile = _active?.FilePath ?? string.Empty;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Oturum", ex);
        }
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed || !_tabs.Any(t => t.IsDirty)) return;
        args.Cancel = true;
        _ = RequestCloseAsync();
    }

    /// <summary>Kaydedilmemiş değişiklik varsa sorar, sonra pencereyi kapatır.</summary>
    private async Task RequestCloseAsync()
    {
        var dirty = _tabs.Where(t => t.IsDirty).ToList();
        if (dirty.Count > 0 && _settings.KeepUnsaved)
        {
            // Metin sekmeleri sorulmadan saklanır, bir sonraki açılışta geri gelir. Belge sekmeleri için yine sorulur.
            while (_backupBusy) await Task.Delay(40);
            if (await WriteBackupsAsync()) dirty = dirty.Where(t => t.Editor is null).ToList();
        }
        if (dirty.Count > 0)
        {
            var choice = await Dialogs.SaveChangesAsync(Root, Theme, dirty.Select(t => t.Title).ToList());
            if (choice == SaveChoice.Cancel) return;
            if (choice == SaveChoice.Save)
            {
                foreach (var tab in dirty)
                {
                    if (!await SaveAsync(tab)) return;
                }
            }
        }
        _closeConfirmed = true;
        Close();
    }

    // ================================================================== komutlar

    private void BuildCommands()
    {
        const VirtualKeyModifiers Ctrl = VirtualKeyModifiers.Control;
        const VirtualKeyModifiers CtrlShift = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift;
        const VirtualKey OemPlus = (VirtualKey)187, OemComma = (VirtualKey)188, OemMinus = (VirtualKey)189;

        Add("New", "Yeni sekme", "Ctrl+N", NewTab, VirtualKey.N, Ctrl);
        Add("Open", "Dosya aç…", "Ctrl+O", () => _ = OpenWithPickerAsync(), VirtualKey.O, Ctrl);
        Add("Save", "Kaydet", "Ctrl+S", () => _ = SaveActiveAsync(false), VirtualKey.S, Ctrl);
        Add("SaveAs", "Farklı kaydet…", "Ctrl+Shift+S", () => _ = SaveActiveAsync(true), VirtualKey.S, CtrlShift);
        Add("SaveAll", "Tümünü kaydet", "", () => _ = SaveAllAsync());
        Add("Duplicate", "Sekmeyi çoğalt", "", () => { if (_active is not null) _ = DuplicateTabAsync(_active); });
        Add("Reveal", "Dosya Gezgini'nde göster", "", RevealActive);
        Add("CopyPath", "Dosya yolunu kopyala", "", CopyActivePath);
        Add("CloseTab", "Sekmeyi kapat", "Ctrl+W", () => { if (_active is not null) _ = CloseTabAsync(_active); }, VirtualKey.W, Ctrl);
        Add("CloseOthers", "Diğer sekmeleri kapat", "", () => _ = CloseManyAsync(_tabs.Where(t => t != _active).ToList()));
        Add("CloseAll", "Tüm sekmeleri kapat", "Ctrl+Shift+W", () => _ = CloseManyAsync(_tabs.ToList()), VirtualKey.W, CtrlShift);
        Add("NextTab", "Sonraki sekme", "Ctrl+Tab", () => StepTab(1), VirtualKey.Tab, Ctrl);
        Add("PreviousTab", "Önceki sekme", "Ctrl+Shift+Tab", () => StepTab(-1), VirtualKey.Tab, CtrlShift);

        Add("OpenFolder", "Klasör aç…", "Ctrl+Shift+O", () => _ = PickExplorerFolderAsync(), VirtualKey.O, CtrlShift);
        Add("Explorer", "Gezgin paneli", "Ctrl+B", ToggleExplorer, VirtualKey.B, Ctrl);
        Add("Find", "Bul", "Ctrl+F", () => _active?.Editor?.Find());
        Add("Replace", "Değiştir", "Ctrl+H", () => _active?.Editor?.Replace());
        Add("GoToLine", "Satıra git", "Ctrl+G", () => _active?.Editor?.GoToLine());
        Add("Format", "Belgeyi biçimlendir", "Shift+Alt+F", () => _ = FormatActiveAsync(), VirtualKey.F, VirtualKeyModifiers.Shift | VirtualKeyModifiers.Menu);
        Add("MarkdownPreview", "Markdown önizleme", "Ctrl+Shift+V", () => _active?.Editor?.TogglePreview(), VirtualKey.V, CtrlShift);
        Add("Minimap", "Küçük harita", "", ToggleMinimap);
        Add("CompactMode", "Kompakt mod", "Ctrl+Alt+K", ToggleCompact, VirtualKey.K, Ctrl | VirtualKeyModifiers.Menu);
        Add("NewDocument", "Yeni belge", "Ctrl+Shift+N", NewDocument, VirtualKey.N, CtrlShift);
        Add("Print", "Yazdır…", "Ctrl+P", () => _active?.Doc?.Print(), VirtualKey.P, Ctrl);
        Add("SessionSave", "Oturumu kaydet", "Ctrl+Alt+S", () => _ = SaveSessionFileAsync(false), VirtualKey.S, Ctrl | VirtualKeyModifiers.Menu);
        Add("SessionSaveAs", "Oturumu farklı kaydet…", "", () => _ = SaveSessionFileAsync(true));
        Add("SessionOpen", "Oturum aç…", "", () => _ = PickSessionAsync());
        Add("SessionClose", "Oturumdan ayrıl", "", () => SetSession(null));
        Add("Undo", "Geri al", "Ctrl+Z", () => { _active?.Editor?.Undo(); _active?.Doc?.Command("undo"); });
        Add("Redo", "Yinele", "Ctrl+Y", () => { _active?.Editor?.Redo(); _active?.Doc?.Command("redo"); });
        Add("Cut", "Kes", "Ctrl+X", () => _active?.Editor?.Cut());
        Add("Copy", "Kopyala", "Ctrl+C", () => _active?.Editor?.Copy());
        Add("Paste", "Yapıştır", "Ctrl+V", () => _active?.Editor?.Paste());
        Add("SelectAll", "Tümünü seç", "Ctrl+A", () => { _active?.Editor?.SelectAll(); _active?.Doc?.Command("selectAll"); });

        Add("TabsHorizontal", "Sekmeler: üstte (yatay)", "", () => SetVerticalTabs(false));
        Add("TabsVertical", "Sekmeler: solda (dikey)", "", () => SetVerticalTabs(true));
        Add("ToggleTabs", "Sekme düzenini değiştir", "Ctrl+Shift+L", () => SetVerticalTabs(!_settings.VerticalTabs), VirtualKey.L, CtrlShift);
        Add("ThemeSystem", "Tema: Windows ayarını kullan", "", () => SetTheme(AppTheme.System));
        Add("ThemeLight", "Tema: açık", "", () => SetTheme(AppTheme.Light));
        Add("ThemeDark", "Tema: koyu", "", () => SetTheme(AppTheme.Dark));
        Add("ToggleTheme", "Açık ve koyu tema arasında geç", "", () => SetTheme(Theme == ElementTheme.Dark ? AppTheme.Light : AppTheme.Dark));
        Add("WordWrap", "Sözcük kaydırma", "Alt+Z", ToggleWordWrap, VirtualKey.Z, VirtualKeyModifiers.Menu);
        Add("StatusBar", "Durum çubuğu", "", ToggleStatusBar);
        Add("ZoomIn", "Yakınlaştır", "Ctrl++", () => ZoomActive(10), OemPlus, Ctrl);
        Add("ZoomOut", "Uzaklaştır", "Ctrl+-", () => ZoomActive(-10), OemMinus, Ctrl);
        Add("ZoomReset", "Yakınlaştırmayı sıfırla", "Ctrl+0", () => ZoomActive(0), VirtualKey.Number0, Ctrl);
        AddAccelerator(VirtualKey.Add, Ctrl, "ZoomIn");
        AddAccelerator(VirtualKey.Subtract, Ctrl, "ZoomOut");
        AddAccelerator(VirtualKey.NumberPad0, Ctrl, "ZoomReset");
        AddAccelerator(VirtualKey.T, Ctrl, "New");

        Add("SqlPanel", "SQL paneli", "Ctrl+E", () => _active?.Data?.ToggleSqlPanel(), VirtualKey.E, Ctrl);
        Add("ColumnProfile", "Kolon profili", "Ctrl+Shift+I", () => _active?.Data?.ToggleProfilePanel(), VirtualKey.I, CtrlShift);
        Add("Export", "Dışa aktar…", "Ctrl+Shift+E", () =>
        {
            if (_active?.Data is { } data) _ = data.ExportAsync();
            else if (_active is { Doc: { } document } tab) _ = ExportDocumentAsync(tab, document);
        }, VirtualKey.E, CtrlShift);
        Add("OpenAsText", "Metin olarak aç", "", () => { if (_active?.Data is not null) _ = ReopenAsync(_active, asText: true); });
        Add("OpenAsTable", "Tablo olarak aç", "", () => { if (_active is { Editor: not null } tab && DataEngine.Supports(tab.FilePath)) _ = ReopenAsync(tab, asText: false); });

        Add("Palette", "Komut paleti", "Ctrl+Shift+P", TogglePalette, VirtualKey.P, CtrlShift, palette: false);
        Add("Settings", "Ayarlar", "Ctrl+,", () => _ = ShowSettingsAsync(), OemComma, Ctrl);
        Add("GitHub", "GitHub sayfası", "", () => _ = Launcher.LaunchUriAsync(new Uri(GitHubUrl)));
        Add("ErrorLog", "Hata günlüğünü aç", "", OpenErrorLog);
        Add("About", "Hakkında", "", () => _ = Dialogs.AboutAsync(Root, Theme));
        Add("Exit", "Çıkış", "Alt+F4", () => _ = RequestCloseAsync());
    }

    private void Add(string id, string title, string shortcut, Action run,
        VirtualKey key = VirtualKey.None, VirtualKeyModifiers modifiers = VirtualKeyModifiers.None, bool palette = true)
    {
        var command = new AppCommand(id, title, shortcut, run, palette);
        _commands[id] = command;
        _commandOrder.Add(command);
        if (key != VirtualKey.None) AddAccelerator(key, modifiers, id);
    }

    private void AddAccelerator(VirtualKey key, VirtualKeyModifiers modifiers, string id)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, e) =>
        {
            e.Handled = true;
            Run(id);
        };
        RootGrid.KeyboardAccelerators.Add(accelerator);

        // Odak kod düzenleyicideyken (WebView2) pencere kısayolları çalışmaz; düzenleyici bunları geri bildirir.
        _editorShortcuts.Add(new EditorShortcut(id, (int)key,
            modifiers.HasFlag(VirtualKeyModifiers.Control), modifiers.HasFlag(VirtualKeyModifiers.Shift), modifiers.HasFlag(VirtualKeyModifiers.Menu)));
    }

    private void Run(string id)
    {
        if (!_commands.TryGetValue(id, out var command)) return;
        try
        {
            command.Run();
        }
        catch (Exception ex)
        {
            CrashLog.Write("Komut " + id, ex);
        }
    }

    private void Command_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id }) Run(id);
    }

    // ================================================================== sekmeler

    private void NewTab()
    {
        var editor = CreateEditor();
        _untitledCount++;
        var tab = new DocumentTab(Loc.F("Adsız {0}", _untitledCount), null, editor);
        HookEditor(tab, editor);
        AddTab(tab);
    }

    private CodeDocumentView CreateEditor()
    {
        var editor = new CodeDocumentView(_editorShortcuts);
        editor.ApplySettings(_settings);
        editor.ShortcutPressed += (_, id) => Run(id);
        return editor;
    }

    private void HookEditor(DocumentTab tab, ITextEditor editor)
    {
        editor.Modified += (_, _) => tab.IsDirty = true;
        editor.CaretMoved += (_, _) =>
        {
            if (tab != _active) return;
            _caretTimer.Stop();
            _caretTimer.Start();
        };
        editor.ZoomChanged += (_, _) =>
        {
            if (tab == _active) UpdateStatus();
        };
    }

    private void AddTab(DocumentTab tab)
    {
        tab.PropertyChanged += Tab_PropertyChanged;
        tab.View.Visibility = Visibility.Collapsed;
        DocumentHost.Children.Add(tab.View);
        _tabs.Add(tab);
        SyncTabViews();
        Activate(tab);
    }

    private void Activate(DocumentTab tab)
    {
        if (!_tabs.Contains(tab)) return;
        if (_active != tab)
        {
            if (_active is not null) _active.View.Visibility = Visibility.Collapsed;
            _active = tab;
            tab.View.Visibility = Visibility.Visible;
        }
        SyncSelection();
        UpdateUi();
        MarkActiveInExplorer();
        tab.Editor?.FocusEditor();
        tab.Data?.FocusGrid();
        tab.Doc?.FocusEditor();
    }

    private void StepTab(int direction)
    {
        if (_tabs.Count < 2 || _active is null) return;
        int index = _tabs.IndexOf(_active);
        Activate(_tabs[(index + direction + _tabs.Count) % _tabs.Count]);
    }

    /// <summary>Sekmeyi kapatır. Kaydedilmemiş değişiklik varsa sorar; vazgeçilirse false döner.</summary>
    private async Task<bool> CloseTabAsync(DocumentTab tab, bool prompt = true)
    {
        if (!_tabs.Contains(tab)) return true;
        if (prompt && tab.IsDirty)
        {
            Activate(tab);
            var choice = await Dialogs.SaveChangesAsync(Root, Theme, new[] { tab.Title });
            if (choice == SaveChoice.Cancel) return false;
            if (choice == SaveChoice.Save && !await SaveAsync(tab)) return false;
            if (!_tabs.Contains(tab)) return true;
        }

        int index = _tabs.IndexOf(tab);
        tab.PropertyChanged -= Tab_PropertyChanged;
        _tabs.Remove(tab);
        _stripItems.Remove(tab);
        DocumentHost.Children.Remove(tab.View);
        tab.Data?.Close();

        if (_active == tab)
        {
            _active = null;
            if (_tabs.Count > 0)
            {
                Activate(_tabs[Math.Min(index, _tabs.Count - 1)]);
                return true;
            }
        }
        SyncTabViews();
        UpdateUi();
        MarkActiveInExplorer();
        return true;
    }

    private async Task CloseManyAsync(List<DocumentTab> tabs)
    {
        var dirty = tabs.Where(t => t.IsDirty).ToList();
        if (dirty.Count > 0)
        {
            var choice = await Dialogs.SaveChangesAsync(Root, Theme, dirty.Select(t => t.Title).ToList());
            if (choice == SaveChoice.Cancel) return;
            if (choice == SaveChoice.Save)
            {
                foreach (var tab in dirty)
                {
                    if (!await SaveAsync(tab)) return;
                }
            }
        }
        foreach (var tab in tabs)
        {
            await CloseTabAsync(tab, prompt: false);
        }
    }

    private void Tab_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DocumentTab tab) return;
        if (_stripItems.TryGetValue(tab, out var item))
        {
            item.Header = tab.DisplayTitle;
            item.IconSource = new FontIconSource { Glyph = tab.Glyph };
            ToolTipService.SetToolTip(item, tab.ToolTip);
        }
        if (tab == _active) UpdateUi();
    }

    private TabViewItem StripItemFor(DocumentTab tab)
    {
        if (_stripItems.TryGetValue(tab, out var existing)) return existing;
        var item = new TabViewItem
        {
            Tag = tab,
            Header = tab.DisplayTitle,
            IconSource = new FontIconSource { Glyph = tab.Glyph },
        };
        ToolTipService.SetToolTip(item, tab.ToolTip);
        item.ContextFlyout = CreateTabMenu(tab);
        _stripItems[tab] = item;
        return item;
    }

    private void Tabs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Dikey listede sürükleyerek sıralama da buraya düşer (çıkar + ekle); şeridi sonradan eşitle.
        if (_syncingTabs || _stripSyncQueued) return;
        _stripSyncQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _stripSyncQueued = false;
            SyncTabViews();
            UpdateUi();
        });
    }

    /// <summary>Listeden görünümlere: yatay şeridi sekme listesiyle aynı sıraya getirir, seçimi eşitler.</summary>
    private void SyncTabViews()
    {
        _syncingTabs = true;
        try
        {
            bool same = TabStrip.TabItems.Count == _tabs.Count;
            for (int i = 0; same && i < _tabs.Count; i++)
            {
                same = TabStrip.TabItems[i] is TabViewItem item && ReferenceEquals(item.Tag, _tabs[i]);
            }
            if (!same)
            {
                TabStrip.TabItems.Clear();
                foreach (var tab in _tabs) TabStrip.TabItems.Add(StripItemFor(tab));
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Sekme şeridi", ex);
        }
        finally
        {
            _syncingTabs = false;
        }
        SyncSelection();
    }

    private void SyncSelection()
    {
        _syncingTabs = true;
        try
        {
            var item = _active is not null && _stripItems.TryGetValue(_active, out var found) && TabStrip.TabItems.Contains(found) ? found : null;
            if (!ReferenceEquals(TabStrip.SelectedItem, item)) TabStrip.SelectedItem = item;
            object? listItem = _active is not null && _tabs.Contains(_active) ? _active : null;
            if (!ReferenceEquals(TabList.SelectedItem, listItem)) TabList.SelectedItem = listItem;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Sekme seçimi", ex);
        }
        finally
        {
            _syncingTabs = false;
        }
    }

    /// <summary>Şeritten listeye: yatay şeritte sürükleyerek yapılan sıralamayı sekme listesine işler.</summary>
    private void QueueAdoptStripOrder()
    {
        if (_syncingTabs || _adoptQueued) return;
        _adoptQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _adoptQueued = false;
            var order = TabStrip.TabItems.OfType<TabViewItem>().Select(i => i.Tag).OfType<DocumentTab>().ToList();
            if (order.Count != _tabs.Count || order.Any(t => !_tabs.Contains(t)))
            {
                SyncTabViews(); // şerit tutarsız: liste kazanır
                return;
            }
            _syncingTabs = true;
            try
            {
                for (int i = 0; i < order.Count; i++)
                {
                    int from = _tabs.IndexOf(order[i]);
                    if (from != i) _tabs.Move(from, i);
                }
            }
            finally
            {
                _syncingTabs = false;
            }
            SyncSelection();
        });
    }

    private void TabStrip_AddTabButtonClick(TabView sender, object args) => NewTab();

    private void TabStrip_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Tab?.Tag is DocumentTab tab) _ = CloseTabAsync(tab);
    }

    private void TabStrip_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingTabs || TabStrip.TabItems.Count != _tabs.Count) return;
        if (TabStrip.SelectedItem is TabViewItem { Tag: DocumentTab tab } && tab != _active) Activate(tab);
    }

    private void TabStrip_TabDragCompleted(TabView sender, TabViewTabDragCompletedEventArgs args) => QueueAdoptStripOrder();

    private void TabStrip_TabItemsChanged(TabView sender, IVectorChangedEventArgs args) => QueueAdoptStripOrder();

    private void TabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingTabs) return;
        if (TabList.SelectedItem is DocumentTab tab && tab != _active) Activate(tab);
    }

    private void VerticalTabClose_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DocumentTab tab }) _ = CloseTabAsync(tab);
    }

    // ================================================================== dosya açma

    private async Task OpenWithPickerAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
            picker.FileTypeFilter.Add("*");
            var files = await picker.PickMultipleFilesAsync();
            if (files is null || files.Count == 0) return;
            await OpenFilesAsync(files.Select(f => f.Path).Where(p => !string.IsNullOrEmpty(p)).ToList());
        }
        catch (Exception ex)
        {
            CrashLog.Write("Dosya seçici", ex);
        }
    }

    public async Task OpenFilesAsync(IReadOnlyList<string> paths, bool reportMissing = true, bool asText = false)
    {
        foreach (var path in paths)
        {
            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch (Exception)
            {
                continue;
            }

            if (SessionFile.IsSession(full))
            {
                await OpenSessionAsync(full);
                continue;
            }

            var existing = _tabs.FirstOrDefault(t => string.Equals(t.FilePath, full, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                Activate(existing);
                continue;
            }
            if (!File.Exists(full))
            {
                _settings.RemoveRecent(full);
                if (reportMissing) await Dialogs.MessageAsync(Root, Theme, Loc.T("Dosya bulunamadı"), full);
                continue;
            }

            try
            {
                var view = await LoadViewAsync(full, asText);
                var tab = new DocumentTab(Path.GetFileName(full), full, view);
                if (view is ITextEditor editor) HookEditor(tab, editor);
                if (view is DataDocumentView data) HookData(tab, data);
                if (view is RichDocumentView document) HookDocument(tab, document);
                tab.TouchDiskStamp();
                AddTab(tab);
                if (view is RichDocumentView loading) _ = LoadDocumentAsync(tab, loading, full);
                if (_explorerRoot is null && _settings.ShowExplorer) RevealInExplorer(full, force: false);
                _settings.AddRecent(full);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                await Dialogs.MessageAsync(Root, Theme, Loc.T("Dosya açılamadı"), full + "\n\n" + ex.Message);
            }
        }
        RefreshRecent();
    }

    /// <summary>Dosya için görünüm kurar: metin düzenleyici ya da (henüz desteklenmeyen biçimlerde) bilgi sayfası.</summary>
    private async Task<FrameworkElement> LoadViewAsync(string path, bool asText)
    {
        var kind = FileKinds.KindOf(path);
        string glyph = FileKinds.Glyph(kind);
        string format = FileKinds.FormatName(path);
        bool binary = FileKinds.IsBinaryFormat(path);

        if (RichDocumentView.Supports(path)) return CreateDocumentView(); // içerik sekme eklendikten sonra yüklenir

        if (DataEngine.Supports(path) && (!asText || binary))
        {
            var data = new DataDocumentView(_hwnd);
            data.ApplySettings(_settings);
            if (await data.LoadAsync(path)) return data;

            string error = data.LoadError ?? string.Empty;
            if (binary) return new PlaceholderView(glyph, Loc.T("Dosya tablo olarak açılamadı"), error);
            await Dialogs.MessageAsync(Root, Theme, Loc.T("Dosya tablo olarak açılamadı"),
                error + "\n\n" + Loc.T("Dosya metin olarak açılıyor."));
        }
        else if (binary)
        {
            return new PlaceholderView(glyph, Loc.F("{0} dosyaları henüz açılamıyor", format),
                kind == DocumentKind.Data
                    ? Loc.T("Bu veri biçimi henüz desteklenmiyor; yol haritasında sırada.")
                    : Loc.T("Bu belge biçimi henüz desteklenmiyor; şimdilik yalnızca Word (.docx) belgeleri açılıyor."));
        }

        long size = new FileInfo(path).Length;
        if (size > TextFileIO.MaxTextBytes)
        {
            return new PlaceholderView(glyph, Loc.T("Dosya şimdilik çok büyük"),
                Loc.F("Düzenleyici en çok {0} MB büyüklüğünde dosya açar.", TextFileIO.MaxTextBytes / (1024 * 1024)));
        }

        var loaded = await Task.Run(() => TextFileIO.Load(path));
        if (loaded is null)
        {
            return new PlaceholderView("", Loc.T("Bu bir metin dosyası değil"),
                Loc.T("Dosya ikili veri içeriyor; metin olarak açmak içeriğini bozabilir."));
        }

        var editor = CreateEditor();
        editor.SetFileName(Path.GetFileName(path));
        editor.Load(loaded);
        return editor;
    }

    private void HookData(DocumentTab tab, DataDocumentView data)
    {
        data.StatusChanged += (_, _) =>
        {
            if (tab == _active) UpdateStatus();
        };
        data.OpenAsTextRequested += (_, _) => _ = ReopenAsync(tab, asText: true);
        data.Notified += (_, message) => Notify(message);
    }

    /// <summary>Sekmeyi kapatıp aynı dosyayı öteki görünümde (tablo ya da ham metin) yeniden açar.</summary>
    private async Task ReopenAsync(DocumentTab tab, bool asText)
    {
        if (tab.FilePath is not { } path) return;
        if (!await CloseTabAsync(tab)) return;
        await OpenFilesAsync(new[] { path }, asText: asText);
    }

    private async Task ReopenWithEncodingAsync(EncodingChoice choice)
    {
        if (_active is not { Editor: { } editor, FilePath: { } path } tab) return;
        if (tab.IsDirty && !await Dialogs.ConfirmAsync(Root, Theme, Loc.T("Yeniden aç"),
                Loc.T("Kaydedilmemiş değişiklikler kaybolacak. Dosya seçilen kodlamayla yeniden açılsın mı?"), Loc.T("Yeniden aç")))
        {
            return;
        }
        try
        {
            var loaded = await Task.Run(() => TextFileIO.Load(path, choice));
            if (loaded is null) return;
            editor.Load(loaded);
            tab.IsDirty = false;
            tab.TouchDiskStamp();
            UpdateUi();
            Notify(Loc.F("{0} olarak yeniden açıldı", Loc.T(choice.Name)), NoticeKind.Info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Dialogs.MessageAsync(Root, Theme, Loc.T("Dosya açılamadı"), path + "\n\n" + ex.Message);
        }
    }

    // ================================================================== kaydetme

    private async Task SaveActiveAsync(bool saveAs)
    {
        if (_active is null) return;
        if (_active.Data is { } data)
        {
            // Veri sekmesi salt okunur: "kaydet" görünen veriyi yeni bir dosyaya aktarır.
            await data.ExportAsync();
            return;
        }
        if (_active.Doc is { } document)
        {
            await SaveDocumentAsync(_active, document, saveAs);
            return;
        }
        await SaveAsync(_active, saveAs);
    }

    private async Task SaveAllAsync()
    {
        int saved = 0;
        foreach (var tab in _tabs.Where(t => t.IsDirty).ToList())
        {
            if (!await SaveAsync(tab)) return;
            saved++;
        }
        if (saved > 1) Notify(Loc.F("{0} sekme kaydedildi", saved));
    }

    private async Task FormatActiveAsync()
    {
        if (_active?.Editor is not { } editor) return;
        string result = await editor.FormatAsync();
        if (result == "ok") Notify(Loc.T("Biçimlendirildi"));
        else if (result == "unsupported") Notify(Loc.T("Bu dil için biçimlendirici yok"), NoticeKind.Warning);
        else Notify(Loc.F("Biçimlendirilemedi: {0}", result.StartsWith("error:", StringComparison.Ordinal) ? result[6..].Trim() : result), NoticeKind.Warning);
    }

    /// <summary>Sekmeyi kaydeder; yolu yoksa ya da <paramref name="saveAs"/> ise yer sorar. Kaydedilemezse false döner.</summary>
    private async Task<bool> SaveAsync(DocumentTab tab, bool saveAs = false)
    {
        if (tab.Doc is { } document) return await SaveDocumentAsync(tab, document, saveAs);
        if (tab.Editor is not { } editor) return true; // bilgi sayfası: kaydedilecek bir şey yok

        string? path = tab.FilePath;
        if (saveAs || path is null)
        {
            Activate(tab);
            path = await PickSavePathAsync(tab);
            if (path is null) return false;
        }

        string text = await editor.GetTextAsync();
        var encoding = editor.Encoding;
        string eol = editor.Eol;
        try
        {
            await Task.Run(() => TextFileIO.Save(path, text, encoding, eol));
        }
        catch (EncoderFallbackException)
        {
            await Dialogs.MessageAsync(Root, Theme, Loc.T("Kaydedilemedi"),
                Loc.F("Metinde {0} kodlamasıyla yazılamayan karakterler var. Durum çubuğundan UTF-8 seçip yeniden dene.", encoding.Name));
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            await Dialogs.MessageAsync(Root, Theme, Loc.T("Kaydedilemedi"), path + "\n\n" + ex.Message);
            return false;
        }

        tab.FilePath = Path.GetFullPath(path);
        tab.IsDirty = false;
        tab.TouchDiskStamp();
        editor.SetFileName(tab.Title);
        _settings.AddRecent(tab.FilePath!);
        RefreshRecent();
        UpdateUi();
        MarkActiveInExplorer();
        Notify(Loc.F("Kaydedildi: {0}", tab.Title));
        return true;
    }

    private async Task<string?> PickSavePathAsync(DocumentTab tab)
    {
        try
        {
            var picker = new FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);

            string extension = tab.FilePath is null ? string.Empty : Path.GetExtension(tab.FilePath);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Choice(string label, string ext)
            {
                if (ext.Length < 2 || !used.Add(ext)) return;
                picker.FileTypeChoices[label + " (*" + ext + ")"] = new List<string> { ext };
            }
            if (extension.Length > 1) Choice(FileKinds.FormatName(tab.FilePath), extension);
            Choice(Loc.T("Metin dosyası"), ".txt");
            Choice("Markdown", ".md");
            Choice("CSV", ".csv");
            Choice("JSON", ".json");
            Choice("XML", ".xml");
            Choice("SQL", ".sql");
            Choice("Python", ".py");
            Choice(Loc.T("Günlük"), ".log");

            picker.SuggestedFileName = tab.FilePath is null ? tab.Title : Path.GetFileNameWithoutExtension(tab.FilePath);
            var file = await picker.PickSaveFileAsync();
            return file?.Path;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Kaydetme seçici", ex);
            return null;
        }
    }

    // ================================================================== küçük eylemler

    private void RevealActive()
    {
        if (_active?.FilePath is { } path) RevealPath(path);
    }

    private void CopyActivePath()
    {
        if (_active?.FilePath is { } path) CopyText(path, Loc.T("Yol kopyalandı"));
    }

    private void OpenErrorLog()
    {
        if (File.Exists(CrashLog.FilePath))
        {
            _ = OpenFilesAsync(new[] { CrashLog.FilePath });
        }
        else
        {
            _ = Dialogs.MessageAsync(Root, Theme, Loc.T("Hata günlüğü"), Loc.T("Kayıtlı hata yok."));
        }
    }

    private async Task ShowSettingsAsync()
    {
        if (await Dialogs.SettingsAsync(Root, Theme, _settings)) ApplySettings();
    }

    // ================================================================== ayarlar ve görünüm

    private void ApplySettings()
    {
        RootGrid.RequestedTheme = ThemeHelper.ToElementTheme(_settings.Theme);
        StatusBar.Visibility = _settings.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
        ApplyDensity(); // görünümler yoğunluğu buradan okur: önce bu
        foreach (var tab in _tabs)
        {
            tab.Editor?.ApplySettings(_settings);
            tab.Doc?.ApplySettings(_settings);
            tab.Data?.ApplySettings(_settings);
        }
        ApplyTabLayout();
        ApplyExplorerLayout();
        SyncMenuState();
        UpdateThemeIcon();
    }

    private void SetVerticalTabs(bool vertical)
    {
        _settings.VerticalTabs = vertical;
        ApplyTabLayout();
        SyncMenuState();
        _active?.Editor?.FocusEditor();
    }

    private void ApplyTabLayout()
    {
        bool vertical = _settings.VerticalTabs;
        TabStrip.Visibility = vertical ? Visibility.Collapsed : Visibility.Visible;
        TitleDragArea.Visibility = vertical ? Visibility.Visible : Visibility.Collapsed;
        TabPane.Visibility = vertical ? Visibility.Visible : Visibility.Collapsed;
        TabSplitter.Visibility = vertical ? Visibility.Visible : Visibility.Collapsed;
        TabPaneColumn.Width = new GridLength(vertical ? Math.Clamp(_settings.TabPaneWidth, 160, 520) : 0);
        try
        {
            // Yatay düzende yalnızca sekmelerin sağındaki boşluk pencereyi sürükler; dikeyde tüm başlık.
            SetTitleBar(vertical ? TitleDragArea : DragRegion);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Başlık çubuğu", ex);
        }
        SyncSelection();
    }

    private void SetTheme(AppTheme theme)
    {
        _settings.Theme = theme;
        RootGrid.RequestedTheme = ThemeHelper.ToElementTheme(theme);
        SyncMenuState();
        UpdateThemeIcon();
    }

    private void ToggleWordWrap()
    {
        _settings.WordWrap = !_settings.WordWrap;
        foreach (var tab in _tabs) tab.Editor?.ApplySettings(_settings);
        SyncMenuState();
    }

    private void ToggleMinimap()
    {
        _settings.ShowMinimap = !_settings.ShowMinimap;
        foreach (var tab in _tabs) tab.Editor?.ApplySettings(_settings);
        SyncMenuState();
    }

    private void ToggleStatusBar()
    {
        _settings.ShowStatusBar = !_settings.ShowStatusBar;
        StatusBar.Visibility = _settings.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
        SyncMenuState();
    }

    private void SyncMenuState()
    {
        MenuTabsHorizontal.IsChecked = !_settings.VerticalTabs;
        MenuTabsVertical.IsChecked = _settings.VerticalTabs;
        MenuThemeSystem.IsChecked = _settings.Theme == AppTheme.System;
        MenuThemeLight.IsChecked = _settings.Theme == AppTheme.Light;
        MenuThemeDark.IsChecked = _settings.Theme == AppTheme.Dark;
        MenuWordWrap.IsChecked = _settings.WordWrap;
        MenuStatusBar.IsChecked = _settings.ShowStatusBar;
        MenuMinimap.IsChecked = _settings.ShowMinimap;
        MenuExplorer.IsChecked = _settings.ShowExplorer;
        MenuCompact.IsChecked = _settings.CompactMode;
    }

    private void UpdateThemeIcon() =>
        ThemeIcon.Glyph = RootGrid.ActualTheme == ElementTheme.Dark ? "" : "";

    private void RootGrid_ActualThemeChanged(FrameworkElement sender, object args)
    {
        WindowHelper.ApplyTitleBarColors(AppWindow, RootGrid.ActualTheme == ElementTheme.Dark);
        UpdateThemeIcon();
    }

    /// <summary>Başlık, karşılama sayfası, menü etkinlikleri ve durum çubuğu.</summary>
    private void UpdateUi()
    {
        string app = SessionName is { } session ? session + " - Zero Data Peek" : "Zero Data Peek";
        string title = _active is null ? app : _active.DisplayTitle + " - " + app;
        Title = title;
        TitleText.Text = title;

        WelcomePanel.Visibility = _tabs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        bool hasTab = _active is not null;
        bool hasEditor = _active?.Editor is not null;
        bool hasPath = _active?.FilePath is not null;
        bool hasData = _active?.Data is not null;
        bool hasDocument = _active?.Doc is not null;
        MenuSave.IsEnabled = hasEditor || hasDocument;
        MenuSaveAs.IsEnabled = hasEditor || hasData || hasDocument;
        MenuFileExport.IsEnabled = hasData || hasDocument;
        MenuPrint.IsEnabled = hasDocument;
        MenuSessionSave.IsEnabled = _tabs.Any(t => t.FilePath is not null);
        MenuSessionSaveAs.IsEnabled = MenuSessionSave.IsEnabled;
        MenuSessionClose.IsEnabled = _sessionPath is not null;
        MenuSqlPanel.IsEnabled = hasData;
        MenuColumnProfile.IsEnabled = hasData;
        MenuExport.IsEnabled = hasData;
        MenuOpenAsText.IsEnabled = hasData;
        MenuOpenAsTable.IsEnabled = hasEditor && hasPath && DataEngine.Supports(_active?.FilePath);
        MenuSaveAll.IsEnabled = _tabs.Any(t => t.IsDirty);
        MenuReveal.IsEnabled = hasPath;
        MenuCopyPath.IsEnabled = hasPath;
        MenuCloseTab.IsEnabled = hasTab;
        MenuCloseOthers.IsEnabled = _tabs.Count > 1;
        MenuCloseAll.IsEnabled = hasTab;
        MenuEdit.IsEnabled = hasEditor || hasDocument;
        MenuDuplicate.IsEnabled = hasEditor;
        MenuFormat.IsEnabled = hasEditor;
        MenuPreview.IsEnabled = hasEditor;

        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var editor = _active?.Editor;
        var data = _active?.Data;
        var document = _active?.Doc;
        var editorVisibility = editor is null ? Visibility.Collapsed : Visibility.Visible;
        StatusCaret.Visibility = editor is null && data is null && document is null ? Visibility.Collapsed : Visibility.Visible;
        ZoomButton.Visibility = editorVisibility;
        EolButton.Visibility = editorVisibility;
        EncodingButton.Visibility = editorVisibility;

        StatusText.Text = _active is null ? Loc.T("Hazır") : _active.FilePath ?? Loc.T("Henüz kaydedilmedi");
        StatusFormat.Text = _active is null ? string.Empty : FileKinds.FormatName(_active.FilePath);
        if (data is not null) StatusCaret.Text = data.StatusText;
        if (document is not null)
        {
            StatusCaret.Text = document.StatusText;
            ZoomButton.Visibility = Visibility.Visible;
            ZoomButton.Content = Loc.F("%{0}", document.Zoom);
        }
        if (editor is null) return;

        var caret = editor.GetCaret();
        StatusCaret.Text = caret.Selected > 0
            ? Loc.F("Sat {0}, Süt {1} · {2} seçili", caret.Line, caret.Column, caret.Selected.ToString("N0", CultureInfo.CurrentCulture))
            : Loc.F("Sat {0}, Süt {1} · {2} karakter", caret.Line, caret.Column, caret.Length.ToString("N0", CultureInfo.CurrentCulture));
        ZoomButton.Content = Loc.F("%{0}", editor.Zoom);
        EolButton.Content = TextFileIO.EolName(editor.Eol);
        EncodingButton.Content = editor.Encoding.Name;
    }

    private void BuildStatusFlyouts()
    {
        var eol = new MenuFlyout();
        foreach (var (label, value) in new[] { ("CRLF (Windows)", "\r\n"), ("LF (Unix, macOS)", "\n"), ("CR", "\r") })
        {
            var item = new MenuFlyoutItem { Text = label };
            item.Click += (_, _) =>
            {
                if (_active is not { Editor: { } editor } tab || editor.Eol == value) return;
                editor.Eol = value;
                tab.IsDirty = true;
                UpdateStatus();
                Notify(Loc.F("Satır sonu: {0} (kaydedince uygulanır)", TextFileIO.EolName(value)), NoticeKind.Info);
            };
            eol.Items.Add(item);
        }
        EolButton.Flyout = eol;

        var reopen = new MenuFlyoutSubItem { Text = Loc.T("Bu kodlamayla yeniden aç") };
        var convert = new MenuFlyoutSubItem { Text = Loc.T("Bu kodlamayla kaydet") };
        foreach (var choice in TextFileIO.Choices)
        {
            var reopenItem = new MenuFlyoutItem { Text = Loc.T(choice.Name) };
            reopenItem.Click += (_, _) => _ = ReopenWithEncodingAsync(choice);
            reopen.Items.Add(reopenItem);

            var convertItem = new MenuFlyoutItem { Text = Loc.T(choice.Name) };
            convertItem.Click += (_, _) =>
            {
                if (_active is not { Editor: { } editor } tab || editor.Encoding == choice) return;
                editor.Encoding = choice;
                tab.IsDirty = true;
                UpdateStatus();
                Notify(Loc.F("Kodlama: {0} (kaydedince uygulanır)", Loc.T(choice.Name)), NoticeKind.Info);
            };
            convert.Items.Add(convertItem);
        }
        var encoding = new MenuFlyout();
        encoding.Opening += (_, _) => reopen.IsEnabled = _active?.FilePath is not null;
        encoding.Items.Add(reopen);
        encoding.Items.Add(convert);
        EncodingButton.Flyout = encoding;
    }

    // ================================================================== son açılanlar

    private void RefreshRecent()
    {
        var recent = _settings.RecentFiles;

        MenuRecent.Items.Clear();
        RecentPanel.Children.Clear();
        RecentHeader.Visibility = recent.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        if (recent.Count == 0)
        {
            MenuRecent.Items.Add(new MenuFlyoutItem { Text = Loc.T("(boş)"), IsEnabled = false });
            return;
        }

        foreach (var path in recent)
        {
            string name = Path.GetFileName(path);
            var menuItem = new MenuFlyoutItem { Text = name };
            ToolTipService.SetToolTip(menuItem, path);
            menuItem.Click += (_, _) => _ = OpenFilesAsync(new[] { path });
            MenuRecent.Items.Add(menuItem);
        }
        MenuRecent.Items.Add(new MenuFlyoutSeparator());
        var clear = new MenuFlyoutItem { Text = Loc.T("Listeyi temizle") };
        clear.Click += (_, _) =>
        {
            _settings.RecentFiles = Array.Empty<string>();
            RefreshRecent();
        };
        MenuRecent.Items.Add(clear);

        foreach (var path in recent.Take(8))
        {
            var link = new HyperlinkButton { Content = Path.GetFileName(path), Padding = new Thickness(6, 4, 6, 4) };
            ToolTipService.SetToolTip(link, path);
            link.Click += (_, _) => _ = OpenFilesAsync(new[] { path });
            RecentPanel.Children.Add(link);
        }
    }

    // ================================================================== sürükle bırak

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        if (e.DragUIOverride is not null) e.DragUIOverride.Caption = Loc.T("Aç");
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.OfType<StorageFile>().Select(f => f.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
            await OpenFilesAsync(paths);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Bırakma", ex);
        }
        finally
        {
            deferral.Complete();
        }
    }

    // ================================================================== komut paleti

    private void TogglePalette()
    {
        if (PaletteOverlay.Visibility == Visibility.Visible) HidePalette();
        else ShowPalette();
    }

    private void ShowPalette()
    {
        PaletteOverlay.Visibility = Visibility.Visible;
        PaletteBox.Text = string.Empty;
        FillPalette(string.Empty);
        PaletteBox.Focus(FocusState.Programmatic);
    }

    private void HidePalette()
    {
        PaletteOverlay.Visibility = Visibility.Collapsed;
        _active?.Editor?.FocusEditor();
    }

    private void FillPalette(string query)
    {
        PaletteList.Items.Clear();
        var compare = CultureInfo.CurrentCulture.CompareInfo;
        query = query.Trim();
        foreach (var command in _commandOrder)
        {
            if (!command.InPalette) continue;
            string title = Loc.T(command.Title);
            if (query.Length > 0 && compare.IndexOf(title, query, CompareOptions.IgnoreCase) < 0) continue;

            var row = new Grid { Tag = command.Id, ColumnSpacing = 16, MinWidth = 480 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis });
            var shortcut = new TextBlock { Text = command.Shortcut, Opacity = 0.6 };
            Grid.SetColumn(shortcut, 1);
            row.Children.Add(shortcut);
            PaletteList.Items.Add(row);
        }
        if (PaletteList.Items.Count > 0) PaletteList.SelectedIndex = 0;
    }

    private void RunPaletteItem(object? item)
    {
        if (item is not FrameworkElement { Tag: string id }) return;
        HidePalette();
        Run(id);
    }

    private void PaletteBox_TextChanged(object sender, TextChangedEventArgs e) => FillPalette(PaletteBox.Text);

    private void PaletteBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        int count = PaletteList.Items.Count;
        switch (e.Key)
        {
            case VirtualKey.Escape:
                HidePalette();
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                RunPaletteItem(PaletteList.SelectedItem);
                e.Handled = true;
                break;
            case VirtualKey.Down when count > 0:
                PaletteList.SelectedIndex = Math.Min(count - 1, PaletteList.SelectedIndex + 1);
                PaletteList.ScrollIntoView(PaletteList.SelectedItem);
                e.Handled = true;
                break;
            case VirtualKey.Up when count > 0:
                PaletteList.SelectedIndex = Math.Max(0, PaletteList.SelectedIndex - 1);
                PaletteList.ScrollIntoView(PaletteList.SelectedItem);
                e.Handled = true;
                break;
        }
    }

    private void PaletteList_ItemClick(object sender, ItemClickEventArgs e) => RunPaletteItem(e.ClickedItem);

    private void PaletteOverlay_Tapped(object sender, TappedRoutedEventArgs e) => HidePalette();

    private void PaletteCard_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;
}
