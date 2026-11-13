using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Core;
using ZeroDataPeek.Controls;
using ZeroDataPeek.Services;

namespace ZeroDataPeek.Views;

/// <summary>
/// Veri modu sekmesi: tablo, arama, sıralama, SQL paneli ve kolon profili.
/// Gösterilen veri üç katmandan biridir: dosyanın kendisi → (varsa) SQL sonucu → (varsa) arama/sıralama görünümü.
/// </summary>
public sealed partial class DataDocumentView : Grid
{
    private readonly DataEngine _engine = DataEngine.Shared;
    private readonly IntPtr _hwnd;
    private readonly DataGridView _grid = new();
    private readonly TextBlock _nameText = new();
    private readonly TextBlock _summaryText = new();
    private readonly TextBox _searchBox = new();
    private readonly TextBox _sqlBox = new();
    private readonly TextBlock _tablesHint = new();
    private readonly Button _backButton = new();
    private readonly Button _clearFiltersButton = new() { Visibility = Visibility.Collapsed };
    private readonly ComboBox _sheetBox = new() { Visibility = Visibility.Collapsed, MinWidth = 120 };
    private readonly List<DataFilter> _filters = new();
    private Button? _textButton;
    private Button? _csvButton;
    private Grid? _toolbar;
    private CsvOptions _csvOptions = new();
    private string _path = string.Empty;
    private bool _settingSheet;
    private readonly InfoBar _info = new() { IsOpen = false, IsClosable = true };
    private readonly ProgressRing _busyRing = new() { IsActive = false, Width = 18, Height = 18, Visibility = Visibility.Collapsed };
    private readonly Border _sqlPanel = new() { Visibility = Visibility.Collapsed };
    private readonly Border _profilePanel = new() { Visibility = Visibility.Collapsed };
    private readonly StackPanel _profileContent = new() { Spacing = 6 };
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _profileTimer;

    private DataRelation? _base;
    private DataRelation? _query;
    private DataRelation? _view;
    private string? _queryName;
    private string? _viewName;
    private string? _search;
    private int _sortColumn = -1;
    private bool _sortDescending;
    private bool _busy;
    private bool _closed;
    private int _profileRequest;

    public DataDocumentView(IntPtr hwnd)
    {
        _hwnd = hwnd;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Children.Add(BuildToolbar());

        _info.Margin = new Thickness(8, 0, 8, 6);
        SetRow(_info, 1);
        Children.Add(_info);

        BuildSqlPanel();
        SetRow(_sqlPanel, 2);
        Children.Add(_sqlPanel);

        var main = new Grid();
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        main.Children.Add(_grid);
        BuildProfilePanel();
        SetColumn(_profilePanel, 1);
        main.Children.Add(_profilePanel);
        SetRow(main, 3);
        Children.Add(main);

        _profileTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        _profileTimer.Interval = TimeSpan.FromMilliseconds(180);
        _profileTimer.IsRepeating = false;
        _profileTimer.Tick += (_, _) => _ = UpdateProfileAsync();

        _grid.HeaderClicked += Grid_HeaderClicked;
        _grid.CurrentCellChanged += (_, _) =>
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
            if (_profilePanel.Visibility == Visibility.Visible)
            {
                _profileTimer.Stop();
                _profileTimer.Start();
            }
        };
        _grid.LoadFailed += (_, message) => ShowInfo(message, InfoBarSeverity.Error);
        _grid.ContextMenuRequested += Grid_ContextMenuRequested;
    }

    /// <summary>Durum çubuğu metni değişti.</summary>
    public event EventHandler? StatusChanged;

    /// <summary>Rahatsız etmeden gösterilecek bir bilgi (ör. dışa aktarma tamamlandı).</summary>
    public event EventHandler<string>? Notified;

    /// <summary>Kullanıcı dosyayı ham metin olarak açmak istedi.</summary>
    public event EventHandler? OpenAsTextRequested;

    /// <summary>Dosya tablo olarak açılamadıysa nedeni.</summary>
    public string? LoadError { get; private set; }

    private DataRelation? Current => _view ?? _query ?? _base;

    /// <summary>SQL panelindeki metin (oturum dosyasına kaydedilir).</summary>
    public string SqlText
    {
        get => _sqlBox.Text;
        set => _sqlBox.Text = value ?? string.Empty;
    }

    /// <summary>Yoğunluk (normal / kompakt) değişince tabloyu ve araç çubuğunu yeniler.</summary>
    public void ApplySettings(AppSettings settings)
    {
        _grid.ApplyDensity();
        if (_toolbar is not null) _toolbar.Padding = Density.Compact ? new Thickness(8, 2, 6, 2) : new Thickness(12, 6, 8, 6);
        _info.Margin = Density.Compact ? new Thickness(4, 0, 4, 2) : new Thickness(8, 0, 8, 6);
    }

    /// <summary>Durum çubuğunda gösterilen konum bilgisi.</summary>
    public string StatusText
    {
        get
        {
            var current = Current;
            if (current is null) return string.Empty;
            if (current.RowCount == 0 || current.Columns.Count == 0) return Loc.T("Satır yok");
            var column = current.Columns[Math.Clamp(_grid.CurrentColumn, 0, current.Columns.Count - 1)];
            return Loc.F("Satır {0} / {1} · {2} ({3})", Number(_grid.CurrentRow + 1), Number(current.RowCount), column.Name, column.Type);
        }
    }

    // ================================================================== yükleme ve kapanış

    /// <summary>Dosyayı tabloya alır. Başarısız olursa false döner ve <see cref="LoadError"/> dolar.</summary>
    public async Task<bool> LoadAsync(string path)
    {
        _path = path;
        if (_textButton is not null) _textButton.Visibility = FileKinds.IsBinaryFormat(path) ? Visibility.Collapsed : Visibility.Visible;
        if (_csvButton is not null)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            _csvButton.Visibility = extension is ".csv" or ".tsv" or ".tab" ? Visibility.Visible : Visibility.Collapsed;
        }
        SetBusy(true);
        try
        {
            var opened = await _engine.OpenFileAsync(path);
            _base = opened.Relation;
            _nameText.Text = opened.Relation.Name;
            if (opened.Note is not null) ShowInfo(opened.Note, InfoBarSeverity.Informational);
            ShowSheets(opened);
            ShowCurrent();
            return true;
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Sekme kapanırken: bu sekmenin tablolarını veritabanından siler.</summary>
    public void Close()
    {
        if (_closed) return;
        _closed = true;
        foreach (var relation in new[] { _view, _query, _base })
        {
            if (relation is null) continue;
            _ = _engine.DropAsync(relation).ContinueWith(t => CrashLog.Write("Tablo silme", t.Exception), TaskContinuationOptions.OnlyOnFaulted);
        }
        _view = _query = _base = null;
    }

    public void FocusGrid() => _grid.Focus(FocusState.Programmatic);

    // ================================================================== Excel sayfaları

    private void ShowSheets(OpenedData opened)
    {
        _settingSheet = true;
        try
        {
            _sheetBox.Items.Clear();
            foreach (var sheet in opened.Sheets) _sheetBox.Items.Add(sheet);
            _sheetBox.Visibility = opened.Sheets.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (opened.Sheets.Count > 0) _sheetBox.SelectedIndex = Math.Clamp(opened.Sheet, 0, opened.Sheets.Count - 1);
        }
        finally
        {
            _settingSheet = false;
        }
    }

    /// <summary>Excel dosyasının başka bir sayfasını aynı tablo adına yükler.</summary>
    private async Task ReloadSheetAsync(int sheet)
    {
        if (_base is null || _busy) return;
        SetBusy(true);
        try
        {
            var query = _query;
            _query = null;
            ResetView();
            await DropViewAsync();
            if (query is not null) await _engine.DropAsync(query);
            var opened = await _engine.OpenFileAsync(_path, sheet, _base.Name, _csvOptions);
            _base = opened.Relation;
            _info.IsOpen = false;
            if (opened.Note is not null) ShowInfo(opened.Note, InfoBarSeverity.Informational);
            ShowCurrent();
        }
        catch (Exception ex)
        {
            ShowInfo(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ShowCsvOptionsAsync()
    {
        if (_busy || XamlRoot is null) return;
        var chosen = await Dialogs.CsvOptionsAsync(XamlRoot, ActualTheme, _csvOptions);
        if (chosen is null) return;
        _csvOptions = chosen;
        await ReloadSheetAsync(0);
    }

    /// <summary>Sıralama, arama ve süzgeçleri sıfırlar (görünüm tablosunu silmez).</summary>
    private void ResetView()
    {
        _sortColumn = -1;
        _search = null;
        _searchBox.Text = string.Empty;
        _filters.Clear();
    }

    // ================================================================== arayüz kurulumu

    private Grid BuildToolbar()
    {
        var bar = new Grid { Padding = new Thickness(12, 6, 8, 6), ColumnSpacing = 10 };
        _toolbar = bar;
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _sheetBox.VerticalAlignment = VerticalAlignment.Center;
        ToolTipService.SetToolTip(_sheetBox, Loc.T("Excel sayfası"));
        _sheetBox.SelectionChanged += (_, _) =>
        {
            if (!_settingSheet && _sheetBox.SelectedIndex >= 0) _ = ReloadSheetAsync(_sheetBox.SelectedIndex);
        };
        SetColumn(_sheetBox, 5);
        bar.Children.Add(_sheetBox);

        _clearFiltersButton.VerticalAlignment = VerticalAlignment.Center;
        _clearFiltersButton.Click += (_, _) =>
        {
            _filters.Clear();
            _ = ApplyViewAsync();
        };
        SetColumn(_clearFiltersButton, 6);
        bar.Children.Add(_clearFiltersButton);

        _nameText.Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"];
        _nameText.VerticalAlignment = VerticalAlignment.Center;
        _nameText.IsTextSelectionEnabled = true;
        ToolTipService.SetToolTip(_nameText, Loc.T("Tablo adı: SQL'de bu adı kullan"));
        bar.Children.Add(_nameText);

        _summaryText.VerticalAlignment = VerticalAlignment.Center;
        _summaryText.Opacity = 0.65;
        _summaryText.TextTrimming = TextTrimming.CharacterEllipsis;
        SetColumn(_summaryText, 1);
        bar.Children.Add(_summaryText);

        _searchBox.PlaceholderText = Loc.T("Tüm kolonlarda ara (Enter)");
        _searchBox.Width = 230;
        _searchBox.VerticalAlignment = VerticalAlignment.Center;
        _searchBox.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Enter) return;
            e.Handled = true;
            string text = _searchBox.Text.Trim();
            _search = text.Length == 0 ? null : text;
            _ = ApplyViewAsync();
        };
        SetColumn(_searchBox, 2);
        bar.Children.Add(_searchBox);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(ToolButton("", "SQL", Loc.T("SQL panelini aç ya da kapat (Ctrl+E)"), ToggleSqlPanel));
        buttons.Children.Add(ToolButton("", Loc.T("Profil"), Loc.T("Seçili kolonun profilini göster (Ctrl+Shift+I)"), ToggleProfilePanel));
        buttons.Children.Add(ToolButton("", Loc.T("Özet"), Loc.T("Tüm kolonların özet istatistikleri"), () => _ = SummarizeAsync()));
        buttons.Children.Add(ToolButton("", Loc.T("Dışa aktar"), Loc.T("Görünen veriyi CSV, Parquet ya da JSON olarak kaydet (Ctrl+Shift+E)"), () => _ = ExportAsync()));
        _textButton = ToolButton("", Loc.T("Metin"), Loc.T("Dosyayı ham metin olarak aç"), () => OpenAsTextRequested?.Invoke(this, EventArgs.Empty));
        buttons.Children.Add(_textButton);
        _csvButton = ToolButton("\uE9E9", "CSV", Loc.T("CSV okuma seçenekleri: ayraç, ondalık, başlık, kodlama"), () => _ = ShowCsvOptionsAsync());
        _csvButton.Visibility = Visibility.Collapsed;
        buttons.Children.Add(_csvButton);
        SetColumn(buttons, 3);
        bar.Children.Add(buttons);

        _busyRing.VerticalAlignment = VerticalAlignment.Center;
        SetColumn(_busyRing, 4);
        bar.Children.Add(_busyRing);
        return bar;
    }

    private static Button ToolButton(string glyph, string text, string tip, Action action)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14 });
        content.Children.Add(new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content };
        if (Application.Current.Resources.TryGetValue("SubtleButton", out var style) && style is Style subtle) button.Style = subtle;
        ToolTipService.SetToolTip(button, tip);
        button.Click += (_, _) => action();
        return button;
    }

    private void BuildSqlPanel()
    {
        _sqlBox.AcceptsReturn = true;
        _sqlBox.IsSpellCheckEnabled = false;
        _sqlBox.TextWrapping = TextWrapping.NoWrap;
        _sqlBox.FontFamily = new FontFamily("Cascadia Mono, Consolas");
        _sqlBox.FontSize = 13;
        _sqlBox.MinHeight = 84;
        _sqlBox.MaxHeight = 220;
        _sqlBox.PlaceholderText = "SELECT …";
        ScrollViewer.SetVerticalScrollBarVisibility(_sqlBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(_sqlBox, ScrollBarVisibility.Auto);
        _sqlBox.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Enter || !IsDown(VirtualKey.Control)) return;
            e.Handled = true;
            _ = RunSqlAsync();
        };

        var run = new Button { Content = Loc.T("Çalıştır") };
        if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var accent) && accent is Style accentStyle) run.Style = accentStyle;
        ToolTipService.SetToolTip(run, "Ctrl+Enter");
        run.Click += (_, _) => _ = RunSqlAsync();

        _backButton.Content = Loc.T("Dosyanın verisine dön");
        _backButton.Visibility = Visibility.Collapsed;
        _backButton.Click += (_, _) => _ = ClearQueryAsync();

        _tablesHint.Opacity = 0.65;
        _tablesHint.FontSize = 12;
        _tablesHint.VerticalAlignment = VerticalAlignment.Center;
        _tablesHint.TextTrimming = TextTrimming.CharacterEllipsis;

        var actions = new Grid { ColumnSpacing = 8 };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var history = new Button { Content = Loc.T("Geçmiş") };
        var historyMenu = new MenuFlyout();
        historyMenu.Opening += (_, _) =>
        {
            historyMenu.Items.Clear();
            var items = SqlHistory.Items;
            if (items.Count == 0)
            {
                historyMenu.Items.Add(new MenuFlyoutItem { Text = Loc.T("(boş)"), IsEnabled = false });
                return;
            }
            foreach (var query in items.Take(25))
            {
                string line = string.Join(" ", query.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(part => part.Trim()));
                var item = new MenuFlyoutItem { Text = line.Length > 80 ? line[..80] + "…" : line };
                item.Click += (_, _) =>
                {
                    _sqlBox.Text = query.Replace("\r\n", "\r").Replace('\n', '\r');
                    _sqlBox.Focus(FocusState.Programmatic);
                };
                historyMenu.Items.Add(item);
            }
        };
        history.Flyout = historyMenu;
        var runGroup = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        runGroup.Children.Add(run);
        runGroup.Children.Add(history);
        actions.Children.Add(runGroup);
        SetColumn(_backButton, 1);
        actions.Children.Add(_backButton);
        SetColumn(_tablesHint, 2);
        actions.Children.Add(_tablesHint);

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(_sqlBox);
        panel.Children.Add(actions);
        _sqlPanel.Padding = new Thickness(12, 0, 12, 10);
        _sqlPanel.Child = panel;
    }

    private void BuildProfilePanel()
    {
        _profilePanel.Width = 290;
        _profilePanel.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray) { Opacity = 0.22 };
        _profilePanel.BorderThickness = new Thickness(1, 0, 0, 0);
        _profilePanel.Child = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(14, 10, 14, 14),
            Content = _profileContent,
        };
    }

    // ================================================================== görünüm

    private void ShowCurrent()
    {
        var current = Current;
        if (current is null) return;
        _grid.SetData(current.Columns, current.RowCount, (offset, count) => _engine.FetchAsync(current, offset, count));
        _grid.SetSort(_view is null ? -1 : _sortColumn, _sortDescending);

        string summary = Loc.F("{0} satır · {1} kolon", Number(current.RowCount), Number(current.Columns.Count));
        if (_query is not null) summary += " · " + Loc.T("sorgu sonucu");
        if (!string.IsNullOrEmpty(_search)) summary += " · " + Loc.F("\"{0}\" araması", _search);
        if (_filters.Count > 0) summary += " · " + Loc.F("{0} süzgeç", _filters.Count);
        _clearFiltersButton.Visibility = _filters.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _clearFiltersButton.Content = Loc.F("Süzgeçleri temizle ({0})", _filters.Count);
        _summaryText.Text = summary;
        _backButton.Visibility = _query is null ? Visibility.Collapsed : Visibility.Visible;

        StatusChanged?.Invoke(this, EventArgs.Empty);
        if (_profilePanel.Visibility == Visibility.Visible) _ = UpdateProfileAsync();
    }

    private async void Grid_HeaderClicked(object? sender, int column)
    {
        if (_busy) return;
        if (_sortColumn != column)
        {
            _sortColumn = column;
            _sortDescending = false;
        }
        else if (!_sortDescending)
        {
            _sortDescending = true;
        }
        else
        {
            _sortColumn = -1;
        }
        await ApplyViewAsync();
    }

    /// <summary>Arama ve sıralamayı uygular: ikisi de yoksa görünümü kaldırır, varsa yeniden üretir.</summary>
    private async Task ApplyViewAsync()
    {
        var source = _query ?? _base;
        if (source is null || _busy) return;
        SetBusy(true);
        try
        {
            bool sorted = _sortColumn >= 0 && _sortColumn < source.Columns.Count;
            if (!sorted && string.IsNullOrEmpty(_search) && _filters.Count == 0)
            {
                await DropViewAsync();
            }
            else
            {
                _viewName ??= _engine.NewInternalName("v");
                _view = await _engine.DeriveAsync(source, _viewName, _search, _filters.ToList(), sorted ? source.Columns[_sortColumn].Name : null, _sortDescending);
            }
            if (_info.Severity == InfoBarSeverity.Error) _info.IsOpen = false;
            ShowCurrent();
        }
        catch (Exception ex)
        {
            ShowInfo(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ================================================================== sağ tık menüsü

    private void Grid_ContextMenuRequested(object? sender, GridContext context)
    {
        var current = Current;
        if (current is null || context.Column < 0 || context.Column >= current.Columns.Count) return;
        var column = current.Columns[context.Column];
        var menu = new MenuFlyout();

        void Item(string text, string glyph, Action action)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        if (context.IsHeader)
        {
            Item(Loc.T("Artan sırala"), "\uE74A", () => SortBy(context.Column, descending: false));
            Item(Loc.T("Azalan sırala"), "\uE74B", () => SortBy(context.Column, descending: true));
            if (_sortColumn >= 0) Item(Loc.T("Sıralamayı kaldır"), "\uE711", () => SortBy(-1, descending: false));
        }
        else
        {
            string? value = _grid.CurrentValue;
            string shown = value is null ? "null" : value.Length > 28 ? value[..28] + "…" : value;
            Item(Loc.F("Süz: {0} = {1}", column.Name, shown), "\uE71C", () => AddFilter(new DataFilter(column.Name, value, false)));
            Item(Loc.F("Hariç tut: {0} ≠ {1}", column.Name, shown), "\uE711", () => AddFilter(new DataFilter(column.Name, value, true)));
            menu.Items.Add(new MenuFlyoutSeparator());
            Item(Loc.T("Hücreyi kopyala"), "\uE8C8", () => _grid.CopySelection(wholeRow: false));
            Item(Loc.T("Satırı kopyala"), "\uE8C8", () => _grid.CopySelection(wholeRow: true));
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        Item(Loc.T("Kolon profili"), "\uE9D9", () =>
        {
            if (_profilePanel.Visibility != Visibility.Visible) ToggleProfilePanel();
        });

        menu.ShowAt(_grid, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions { Position = context.Position });
    }

    private void SortBy(int column, bool descending)
    {
        _sortColumn = column;
        _sortDescending = descending;
        _ = ApplyViewAsync();
    }

    private void AddFilter(DataFilter filter)
    {
        _filters.Add(filter);
        _ = ApplyViewAsync();
    }

    private async Task DropViewAsync()
    {
        var view = _view;
        _view = null;
        if (view is not null) await _engine.DropAsync(view);
    }

    // ================================================================== SQL

    public void ToggleSqlPanel()
    {
        bool show = _sqlPanel.Visibility != Visibility.Visible;
        _sqlPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
        {
            FocusGrid();
            return;
        }
        if (string.IsNullOrWhiteSpace(_sqlBox.Text) && _base is not null)
        {
            _sqlBox.Text = "SELECT *\rFROM " + _base.Name + "\rLIMIT 100";
        }
        _tablesHint.Text = Loc.F("Açık tablolar: {0}", string.Join(", ", _engine.TableNames));
        _sqlBox.Focus(FocusState.Programmatic);
    }

    private async Task RunSqlAsync()
    {
        string sql = _sqlBox.Text;
        if (string.IsNullOrWhiteSpace(sql) || _busy) return;
        SetBusy(true);
        try
        {
            _queryName ??= _engine.NewInternalName("q");
            _query = await _engine.QueryAsync(sql, _queryName);
            SqlHistory.Add(sql.Replace("\r\n", "\n").Replace('\r', '\n'));
            ResetView();
            await DropViewAsync();
            _info.IsOpen = false;
            ShowCurrent();
        }
        catch (Exception ex)
        {
            ShowInfo(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ClearQueryAsync()
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var query = _query;
            _query = null;
            ResetView();
            await DropViewAsync();
            if (query is not null) await _engine.DropAsync(query);
            _info.IsOpen = false;
            ShowCurrent();
        }
        catch (Exception ex)
        {
            ShowInfo(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task SummarizeAsync()
    {
        if (_base is null) return;
        if (_sqlPanel.Visibility != Visibility.Visible) ToggleSqlPanel();
        _sqlBox.Text = "SUMMARIZE " + _base.Name;
        await RunSqlAsync();
    }

    // ================================================================== kolon profili

    public void ToggleProfilePanel()
    {
        bool show = _profilePanel.Visibility != Visibility.Visible;
        _profilePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) _ = UpdateProfileAsync();
    }

    private async Task UpdateProfileAsync()
    {
        var current = Current;
        if (current is null || current.Columns.Count == 0) return;
        var column = current.Columns[Math.Clamp(_grid.CurrentColumn, 0, current.Columns.Count - 1)];
        int request = ++_profileRequest;
        try
        {
            var profile = await _engine.ProfileAsync(current, column);
            if (request != _profileRequest || _closed) return;
            ShowProfile(profile);
        }
        catch (Exception ex)
        {
            if (request != _profileRequest || _closed) return;
            _profileContent.Children.Clear();
            _profileContent.Children.Add(new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap });
        }
    }

    private void ShowProfile(ColumnProfile profile)
    {
        _profileContent.Children.Clear();
        _profileContent.Children.Add(new TextBlock
        {
            Text = profile.Column.Name,
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        _profileContent.Children.Add(new TextBlock { Text = profile.Column.Type, Opacity = 0.65, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) });

        long nulls = Math.Max(0, profile.Rows - profile.NonNull);
        AddStat(Loc.T("Satır"), Number(profile.Rows));
        AddStat(Loc.T("Dolu"), Number(profile.NonNull) + Percent(profile.NonNull, profile.Rows));
        AddStat(Loc.T("Boş"), Number(nulls) + Percent(nulls, profile.Rows));
        AddStat(Loc.T("Tekil (yaklaşık)"), Number(profile.Distinct));
        if (profile.Min is not null) AddStat(Loc.T("En küçük"), profile.Min);
        if (profile.Max is not null) AddStat(Loc.T("En büyük"), profile.Max);
        if (profile.Average is not null) AddStat(Loc.T("Ortalama"), profile.Average);
        if (profile.Median is not null) AddStat(Loc.T("Ortanca"), profile.Median);

        if (profile.TopValues.Count == 0) return;
        _profileContent.Children.Add(new TextBlock
        {
            Text = Loc.T("En sık değerler"),
            Margin = new Thickness(0, 10, 0, 0),
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        foreach (var (value, count) in profile.TopValues)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock { Text = value ?? "null", FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
            if (value is null)
            {
                label.Opacity = 0.5;
                label.FontStyle = Windows.UI.Text.FontStyle.Italic;
            }
            else
            {
                ToolTipService.SetToolTip(label, value.Length > 400 ? value[..400] + "…" : value);
            }
            row.Children.Add(label);
            var amount = new TextBlock { Text = Number(count), FontSize = 12, Opacity = 0.65 };
            SetColumn(amount, 1);
            row.Children.Add(amount);
            _profileContent.Children.Add(row);
            _profileContent.Children.Add(new ProgressBar
            {
                Minimum = 0,
                Maximum = Math.Max(1, profile.Rows),
                Value = count,
                Margin = new Thickness(0, 0, 0, 4),
            });
        }
    }

    private void AddStat(string label, string value)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = label, FontSize = 12, Opacity = 0.65 });
        var text = new TextBlock
        {
            Text = value.Length > 200 ? value[..200] + "…" : value,
            FontSize = 12,
            TextAlignment = TextAlignment.Right,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        SetColumn(text, 1);
        row.Children.Add(text);
        _profileContent.Children.Add(row);
    }

    // ================================================================== dışa aktarma

    /// <summary>Görünen veriyi (sorgu, arama ve sıralama uygulanmış haliyle) dosyaya yazar.</summary>
    public async Task ExportAsync()
    {
        var current = Current;
        if (current is null || _busy) return;

        string? path;
        try
        {
            var picker = new FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
            picker.FileTypeChoices["Excel (*.xlsx)"] = new List<string> { ".xlsx" };
            picker.FileTypeChoices["CSV (*.csv)"] = new List<string> { ".csv" };
            picker.FileTypeChoices["Parquet (*.parquet)"] = new List<string> { ".parquet" };
            picker.FileTypeChoices["JSON (*.json)"] = new List<string> { ".json" };
            picker.FileTypeChoices["JSON Lines (*.jsonl)"] = new List<string> { ".jsonl" };
            picker.FileTypeChoices["TSV (*.tsv)"] = new List<string> { ".tsv" };
            picker.SuggestedFileName = (_base?.Name ?? "veri") + (_query is null && _view is null ? string.Empty : "_sonuc");
            var file = await picker.PickSaveFileAsync();
            path = file?.Path;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Dışa aktarma seçici", ex);
            return;
        }
        if (string.IsNullOrEmpty(path)) return;

        string chosenExtension = Path.GetExtension(path).ToLowerInvariant();
        if (chosenExtension == ".xlsx")
        {
            SetBusy(true);
            try
            {
                var (rows, truncated) = await _engine.ExportXlsxAsync(current, path);
                if (truncated)
                {
                    ShowInfo(Loc.F("Excel bir sayfada en çok {0} satır tutar; ilk {1} satır yazıldı: {2}", Number(XlsxWriter.MaxRows), Number(rows), path), InfoBarSeverity.Warning);
                }
                else
                {
                    Notified?.Invoke(this, Loc.F("{0} satır dışa aktarıldı: {1}", Number(rows), path));
                }
            }
            catch (Exception ex)
            {
                ShowInfo(ex.Message, InfoBarSeverity.Error);
            }
            finally
            {
                SetBusy(false);
            }
            return;
        }

        var format = chosenExtension switch
        {
            ".parquet" => ExportFormat.Parquet,
            ".json" => ExportFormat.Json,
            ".jsonl" or ".ndjson" => ExportFormat.JsonLines,
            ".tsv" or ".tab" => ExportFormat.Tsv,
            _ => ExportFormat.Csv,
        };

        SetBusy(true);
        try
        {
            await _engine.ExportAsync(current, path, format);
            Notified?.Invoke(this, Loc.F("{0} satır dışa aktarıldı: {1}", Number(current.RowCount), path));
        }
        catch (Exception ex)
        {
            ShowInfo(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ================================================================== yardımcılar

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _busyRing.IsActive = busy;
        _busyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowInfo(string message, InfoBarSeverity severity)
    {
        _info.Severity = severity;
        _info.Message = message.Length > 900 ? message[..900] + "…" : message;
        _info.IsOpen = true;
    }

    private static string Number(long value) => value.ToString("N0", CultureInfo.CurrentCulture);

    private static string Percent(long part, long total) =>
        total <= 0 ? string.Empty : " (" + (part * 100.0 / total).ToString("0.#", CultureInfo.CurrentCulture) + "%)";

    private static bool IsDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
