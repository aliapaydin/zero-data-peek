using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.System;
using ZeroDataPeek.Models;
using ZeroDataPeek.Services;

namespace ZeroDataPeek.Views;

/// <summary>
/// "Dosyalarda ara" sekmesi: bir klasördeki metin dosyalarında arama yapar, sonuçları dosyaya göre gruplar.
/// Sonuca tıklanınca dosya açılır ve eşleşme seçilir (bkz. <see cref="OpenRequested"/>).
/// </summary>
public sealed partial class SearchView : Grid
{
    private readonly Func<Task<string?>> _pickFolder;
    private readonly ObservableCollection<SearchHit> _hits = new();
    private readonly TextBox _queryBox = new(), _includeBox = new(), _excludeBox = new();
    private readonly ToggleButton _caseButton = new(), _wordButton = new(), _regexButton = new();
    private readonly Button _searchButton = new();
    private readonly TextBlock _folderText = new(), _summary = new();
    private readonly ProgressRing _ring = new() { IsActive = false, Width = 16, Height = 16, Visibility = Visibility.Collapsed };
    private readonly ListView _list = new();
    private readonly StackPanel _form = new() { Spacing = 8 };
    private CancellationTokenSource? _cancel;
    private string _folder = string.Empty;
    private int _generation;

    public SearchView(DataTemplate hitTemplate, Func<Task<string?>> pickFolder)
    {
        _pickFolder = pickFolder;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        BuildForm();
        Children.Add(_form);

        _list.ItemsSource = _hits;
        _list.ItemTemplate = hitTemplate;
        _list.SelectionMode = ListViewSelectionMode.None;
        _list.IsItemClickEnabled = true;
        _list.Padding = new Thickness(4, 0, 4, 8);
        _list.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is SearchHit hit) OpenRequested?.Invoke(this, hit);
        };
        SetRow(_list, 1);
        Children.Add(_list);
        ApplySettings();
    }

    /// <summary>Bir sonuca tıklandı: dosya başlığıysa dosyanın başı, eşleşmeyse o satır açılmalı.</summary>
    public event EventHandler<SearchHit>? OpenRequested;

    /// <summary>Aramanın yapılacağı klasör.</summary>
    public string Folder
    {
        get => _folder;
        set
        {
            _folder = value ?? string.Empty;
            _folderText.Text = _folder.Length == 0 ? Loc.T("Klasör seçilmedi") : _folder;
            ToolTipService.SetToolTip(_folderText, _folder.Length == 0 ? null : _folder);
        }
    }

    public string StatusText => _summary.Text;

    public void FocusQuery()
    {
        _queryBox.Focus(FocusState.Programmatic);
        _queryBox.SelectAll();
    }

    public void SetQuery(string text)
    {
        if (!string.IsNullOrWhiteSpace(text)) _queryBox.Text = text;
    }

    /// <summary>Yoğunluk değişince satırları yeniden kurar.</summary>
    public void ApplySettings()
    {
        _form.Padding = Density.Compact ? new Thickness(10, 6, 10, 4) : new Thickness(16, 12, 16, 8);
        _form.Spacing = Density.Compact ? 4 : 8;
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(MinHeightProperty, Density.Compact ? 20.0 : 26.0));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 0, 10, 0)));
        _list.ItemContainerStyle = style;
        _list.ItemsSource = null;
        _list.ItemsSource = _hits;
    }

    private void BuildForm()
    {
        // 1. satır: aranacak metin + düğme
        var first = new Grid { ColumnSpacing = 8 };
        first.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        first.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _queryBox.PlaceholderText = Loc.T("Aranacak metin (Enter)");
        _queryBox.IsSpellCheckEnabled = false;
        _queryBox.KeyDown += Box_KeyDown;
        first.Children.Add(_queryBox);
        _searchButton.Content = Loc.T("Ara");
        _searchButton.MinWidth = 90;
        if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var accent) && accent is Style accentStyle) _searchButton.Style = accentStyle;
        _searchButton.Click += (_, _) =>
        {
            if (_cancel is not null) _cancel.Cancel();
            else _ = RunAsync();
        };
        SetColumn(_searchButton, 1);
        first.Children.Add(_searchButton);
        _form.Children.Add(first);

        // 2. satır: seçenekler ve dosya süzgeçleri
        var second = new Grid { ColumnSpacing = 6 };
        second.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        second.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        second.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var toggles = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        Toggle(_caseButton, "Aa", Loc.T("Büyük / küçük harfe duyarlı"));
        Toggle(_wordButton, Loc.T("Tam sözcük"), Loc.T("Yalnızca tam sözcük eşleşmeleri"));
        Toggle(_regexButton, ".*", Loc.T("Düzenli ifade (regex) olarak ara"));
        toggles.Children.Add(_caseButton);
        toggles.Children.Add(_wordButton);
        toggles.Children.Add(_regexButton);
        second.Children.Add(toggles);
        _includeBox.PlaceholderText = Loc.T("Yalnızca şu dosyalar: *.sql; *.py");
        _includeBox.IsSpellCheckEnabled = false;
        _includeBox.KeyDown += Box_KeyDown;
        SetColumn(_includeBox, 1);
        second.Children.Add(_includeBox);
        _excludeBox.PlaceholderText = Loc.T("Şunlar hariç: *.min.js; *.log");
        _excludeBox.IsSpellCheckEnabled = false;
        _excludeBox.KeyDown += Box_KeyDown;
        SetColumn(_excludeBox, 2);
        second.Children.Add(_excludeBox);
        _form.Children.Add(second);

        // 3. satır: klasör
        var third = new Grid { ColumnSpacing = 8 };
        third.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        third.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        third.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        third.Children.Add(new FontIcon { Glyph = "", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
        _folderText.VerticalAlignment = VerticalAlignment.Center;
        _folderText.TextTrimming = TextTrimming.CharacterEllipsis;
        _folderText.Text = Loc.T("Klasör seçilmedi");
        SetColumn(_folderText, 1);
        third.Children.Add(_folderText);
        var change = new Button { Content = Loc.T("Klasör değiştir…") };
        change.Click += async (_, _) =>
        {
            string? picked = await _pickFolder();
            if (!string.IsNullOrEmpty(picked)) Folder = picked;
        };
        SetColumn(change, 2);
        third.Children.Add(change);
        _form.Children.Add(third);

        // 4. satır: özet
        var fourth = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, MinHeight = 20 };
        fourth.Children.Add(_ring);
        _summary.Opacity = 0.7;
        _summary.FontSize = 12;
        _summary.VerticalAlignment = VerticalAlignment.Center;
        _summary.Text = Loc.T("Alt klasörler de aranır. İkili dosyalar ile .git, node_modules, bin, obj gibi klasörler atlanır.");
        _summary.TextWrapping = TextWrapping.Wrap;
        fourth.Children.Add(_summary);
        _form.Children.Add(fourth);
    }

    private static void Toggle(ToggleButton button, string text, string tip)
    {
        button.Content = text;
        button.MinWidth = 38;
        button.Padding = new Thickness(8, 4, 8, 4);
        ToolTipService.SetToolTip(button, tip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tip);
    }

    private void Box_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        _ = RunAsync();
    }

    /// <summary>Aramayı başlatır; süren bir arama varsa önce onu durdurur.</summary>
    public async Task RunAsync()
    {
        string query = _queryBox.Text;
        if (query.Length == 0) return;
        if (_folder.Length == 0 || !Directory.Exists(_folder))
        {
            _summary.Text = Loc.T("Önce aranacak klasörü seç.");
            return;
        }

        var options = new SearchOptions(_folder, query, _caseButton.IsChecked == true, _wordButton.IsChecked == true, _regexButton.IsChecked == true,
            _includeBox.Text, _excludeBox.Text);
        try
        {
            FileSearch.BuildPattern(options);
        }
        catch (ArgumentException ex)
        {
            _summary.Text = Loc.F("Düzenli ifade geçersiz: {0}", ex.Message);
            return;
        }

        _cancel?.Cancel();
        int generation = ++_generation;
        var cancel = new CancellationTokenSource();
        _cancel = cancel;
        _hits.Clear();
        _ring.IsActive = true;
        _ring.Visibility = Visibility.Visible;
        _searchButton.Content = Loc.T("Durdur");
        _summary.Text = Loc.T("Aranıyor…");
        var queue = DispatcherQueue;

        try
        {
            var summary = await Task.Run(() => FileSearch.Run(options, hits =>
            {
                queue.TryEnqueue(() =>
                {
                    if (generation != _generation) return;
                    foreach (var hit in hits) _hits.Add(hit);
                });
            }, cancel.Token));
            if (generation != _generation) return;

            // Son dosyaların listeye eklenmesi sıraya girmiş olabilir: özeti onlardan sonra yaz.
            queue.TryEnqueue(() =>
            {
                if (generation != _generation) return;
                _summary.Text = summary.Matches == 0
                    ? Loc.F("Eşleşme yok ({0} dosya tarandı)", summary.FilesScanned)
                    : summary.Truncated
                        ? Loc.F("İlk {0} eşleşme gösteriliyor ({1} dosyada); aramayı daralt", summary.Matches, summary.FilesMatched)
                        : Loc.F("{0} eşleşme, {1} dosyada ({2} dosya tarandı)", summary.Matches, summary.FilesMatched, summary.FilesScanned);
            });
        }
        catch (OperationCanceledException)
        {
            if (generation == _generation) _summary.Text = Loc.T("Arama durduruldu.");
        }
        catch (Exception ex)
        {
            CrashLog.Write("Dosyalarda arama", ex);
            if (generation == _generation) _summary.Text = Loc.F("Arama tamamlanamadı: {0}", ex.Message);
        }
        finally
        {
            if (generation == _generation)
            {
                _cancel = null;
                _ring.IsActive = false;
                _ring.Visibility = Visibility.Collapsed;
                _searchButton.Content = Loc.T("Ara");
            }
            cancel.Dispose();
        }
    }
}
