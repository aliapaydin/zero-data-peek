using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZeroDataPeek.Services;

namespace ZeroDataPeek.Views;

public enum SaveChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>Kod ile kurulan küçük diyaloglar.</summary>
public static class Dialogs
{
    private static bool _open;

    public static ContentDialog Create(XamlRoot root, ElementTheme theme, string title, object content,
        string? primary = null, string? close = null, ContentDialogButton defaultButton = ContentDialogButton.Close)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = theme,
            Title = title,
            Content = content,
            CloseButtonText = close ?? Loc.T("Kapat"),
            DefaultButton = defaultButton,
        };
        if (primary is not null) dialog.PrimaryButtonText = primary;
        return dialog;
    }

    /// <summary>Aynı anda yalnızca bir diyalog açılabilir; ikincisi yok sayılır (None döner).</summary>
    public static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        if (_open) return ContentDialogResult.None;
        _open = true;
        try
        {
            return await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            CrashLog.Write("Diyalog", ex);
            return ContentDialogResult.None;
        }
        finally
        {
            _open = false;
        }
    }

    public static async Task MessageAsync(XamlRoot root, ElementTheme theme, string title, string message) =>
        await ShowAsync(Create(root, theme, title,
            new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }, close: Loc.T("Tamam")));

    public static async Task<bool> ConfirmAsync(XamlRoot root, ElementTheme theme, string title, string message, string confirm)
    {
        var dialog = Create(root, theme, title, new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            confirm, Loc.T("Vazgeç"), ContentDialogButton.Primary);
        return await ShowAsync(dialog) == ContentDialogResult.Primary;
    }

    /// <summary>Kaydedilmemiş değişiklikler için: Kaydet / Kaydetme / Vazgeç.</summary>
    public static async Task<SaveChoice> SaveChangesAsync(XamlRoot root, ElementTheme theme, IReadOnlyList<string> names)
    {
        string message = names.Count == 1
            ? Loc.F("\"{0}\" dosyasındaki değişiklikler kaydedilsin mi?", names[0])
            : Loc.F("{0} sekmede kaydedilmemiş değişiklik var. Kaydedilsin mi?", names.Count) + "\n\n" + string.Join("\n", names.Take(12));
        var dialog = Create(root, theme, Loc.T("Kaydedilmemiş değişiklikler"),
            new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            Loc.T("Kaydet"), Loc.T("Vazgeç"), ContentDialogButton.Primary);
        dialog.SecondaryButtonText = Loc.T("Kaydetme");
        return await ShowAsync(dialog) switch
        {
            ContentDialogResult.Primary => SaveChoice.Save,
            ContentDialogResult.Secondary => SaveChoice.Discard,
            _ => SaveChoice.Cancel,
        };
    }

    // ================================================================== ayarlar

    /// <summary>Ayarlar: solda bölümler, sağda seçili bölümün kartları. Kaydedilirse true döner; çağıran taraf ayarları yeniden uygular.</summary>
    public static async Task<bool> SettingsAsync(XamlRoot root, ElementTheme theme, AppSettings settings)
    {
        // ---- Görünüm
        var themeBox = Combo(new[] { Loc.T("Windows ayarını kullan"), Loc.T("Açık tema"), Loc.T("Koyu tema") }, (int)settings.Theme);
        var languageBox = Combo(new[] { Loc.T("Windows diline göre"), "Türkçe", "English" },
            settings.UiLanguage switch { "tr" => 1, "en" => 2, _ => 0 });
        var tabsBox = Combo(new[] { Loc.T("Üstte (yatay)"), Loc.T("Solda (dikey)") }, settings.VerticalTabs ? 1 : 0);
        var compactSwitch = Switch(settings.CompactMode);
        var statusSwitch = Switch(settings.ShowStatusBar);
        var explorerSwitch = Switch(settings.ShowExplorer);
        var appearance = Section(
            Card(Loc.T("Tema"), null, themeBox),
            Card(Loc.T("Dil"), Loc.T("Dil değişikliği uygulama yeniden başlatılınca geçerli olur."), languageBox),
            Card(Loc.T("Sekmeler"), Loc.T("Sekmelerin yeri: başlık çubuğunda ya da solda liste olarak."), tabsBox),
            Card(Loc.T("Kompakt mod"), Loc.T("Satırları alçaltır, yazıları küçültür; ekrana daha çok şey sığar."), compactSwitch),
            Card(Loc.T("Gezgin paneli"), Loc.T("Soldaki klasör ağacı."), explorerSwitch),
            Card(Loc.T("Durum çubuğu"), null, statusSwitch));

        // ---- Düzenleyici
        var fonts = new List<string> { "Cascadia Mono", "Cascadia Code", "Consolas", "Courier New", "Lucida Console", "Segoe UI" };
        if (!fonts.Contains(settings.FontFamily, StringComparer.OrdinalIgnoreCase)) fonts.Insert(0, settings.FontFamily);
        var fontBox = Combo(fonts, fonts.FindIndex(f => string.Equals(f, settings.FontFamily, StringComparison.OrdinalIgnoreCase)));
        var sizeBox = new NumberBox
        {
            Value = settings.FontSize,
            Minimum = 8,
            Maximum = 48,
            SmallChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            MinWidth = 150,
        };
        var wrapSwitch = Switch(settings.WordWrap);
        var minimapSwitch = Switch(settings.ShowMinimap);
        var editor = Section(
            Card(Loc.T("Yazı tipi"), null, fontBox),
            Card(Loc.T("Yazı boyutu"), null, sizeBox),
            Card(Loc.T("Sözcük kaydırma"), Loc.T("Uzun satırları pencere genişliğinde alt satıra böler."), wrapSwitch),
            Card(Loc.T("Küçük harita"), Loc.T("Kod düzenleyicinin sağındaki belge özeti."), minimapSwitch));

        // ---- Oturum
        var sessionSwitch = Switch(settings.RestoreSession);
        var unsavedSwitch = Switch(settings.KeepUnsaved);
        var session = Section(
            Card(Loc.T("Açılışta son oturumu geri yükle"), Loc.T("Kapanışta açık olan dosyalar bir sonraki açılışta geri gelir."), sessionSwitch),
            Card(Loc.T("Kaydedilmemiş sekmeleri sakla"), Loc.T("Kapanırken kaydetmeyi sormaz; yazdıkların bir sonraki açılışta geri gelir. Yalnızca metin ve kod sekmeleri için."), unsavedSwitch));

        // ---- Sağ tık menüsü
        var enabledTypes = new HashSet<string>(ShellMenu.Enabled(settings), StringComparer.OrdinalIgnoreCase);
        var typeBoxes = new List<CheckBox>();
        var shell = new StackPanel { Spacing = 8 };
        shell.Children.Add(new TextBlock
        {
            Text = Loc.T("İşaretli türlerde, dosyaya sağ tıklayınca \"Zero Data Peek ile aç\" seçeneği görünür."),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
        });
        var all = new Button { Content = Loc.T("Tümünü seç") };
        var none = new Button { Content = Loc.T("Hiçbirini seçme") };
        all.Click += (_, _) => typeBoxes.ForEach(b => b.IsChecked = true);
        none.Click += (_, _) => typeBoxes.ForEach(b => b.IsChecked = false);
        var bulk = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        bulk.Children.Add(all);
        bulk.Children.Add(none);
        shell.Children.Add(bulk);
        foreach (var (group, extensions) in ShellMenu.Groups)
        {
            var grid = new Grid { ColumnSpacing = 4 };
            for (int column = 0; column < 5; column++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 0; i < extensions.Length; i++)
            {
                if (i % 5 == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var box = new CheckBox { Content = extensions[i], Tag = extensions[i], IsChecked = enabledTypes.Contains(extensions[i]), MinWidth = 0 };
                Grid.SetRow(box, i / 5);
                Grid.SetColumn(box, i % 5);
                grid.Children.Add(box);
                typeBoxes.Add(box);
            }
            var groupPanel = new StackPanel { Spacing = 2 };
            groupPanel.Children.Add(new TextBlock { Text = Loc.T(group), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            groupPanel.Children.Add(grid);
            shell.Children.Add(Boxed(groupPanel));
        }

        var sections = new (string Title, string Glyph, FrameworkElement Content)[]
        {
            (Loc.T("Görünüm"), "", appearance),
            (Loc.T("Düzenleyici"), "", editor),
            (Loc.T("Oturum"), "", session),
            (Loc.T("Sağ tık menüsü"), "", shell),
        };

        var content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 14, 0),
        };
        var navigation = new ListView { SelectionMode = ListViewSelectionMode.Single };
        foreach (var (title, glyph, _) in sections)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            item.Children.Add(new FontIcon { Glyph = glyph, FontSize = 15 });
            item.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
            navigation.Items.Add(item);
        }
        navigation.SelectionChanged += (_, _) =>
        {
            int index = navigation.SelectedIndex;
            if (index >= 0 && index < sections.Length) content.Content = sections[index].Content;
        };
        navigation.SelectedIndex = 0;

        var layout = new Grid { Width = 780, Height = 460, ColumnSpacing = 14 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(navigation);
        Grid.SetColumn(content, 1);
        layout.Children.Add(content);

        var dialog = Create(root, theme, Loc.T("Ayarlar"), layout, Loc.T("Kaydet"), Loc.T("Vazgeç"), ContentDialogButton.Primary);
        Widen(dialog, 880);
        if (await ShowAsync(dialog) != ContentDialogResult.Primary) return false;

        settings.Theme = (AppTheme)Math.Max(0, themeBox.SelectedIndex);
        settings.UiLanguage = languageBox.SelectedIndex switch { 1 => "tr", 2 => "en", _ => string.Empty };
        settings.VerticalTabs = tabsBox.SelectedIndex == 1;
        settings.CompactMode = compactSwitch.IsOn;
        settings.ShowStatusBar = statusSwitch.IsOn;
        settings.ShowExplorer = explorerSwitch.IsOn;
        if (fontBox.SelectedItem is string font) settings.FontFamily = font;
        if (!double.IsNaN(sizeBox.Value)) settings.FontSize = Math.Clamp(sizeBox.Value, 8, 48);
        settings.WordWrap = wrapSwitch.IsOn;
        settings.ShowMinimap = minimapSwitch.IsOn;
        settings.RestoreSession = sessionSwitch.IsOn;
        settings.KeepUnsaved = unsavedSwitch.IsOn;
        ShellMenu.Save(settings, typeBoxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag));
        return true;
    }

    /// <summary>ContentDialog varsayılan olarak dardır (548 px); geniş içerik için üst sınırı yükseltir.</summary>
    private static void Widen(ContentDialog dialog, double width)
    {
        dialog.Resources["ContentDialogMaxWidth"] = width;
        dialog.Resources["ContentDialogMaxHeight"] = 760.0;
    }

    private static StackPanel Section(params FrameworkElement[] cards)
    {
        var panel = new StackPanel { Spacing = 6 };
        foreach (var card in cards) panel.Children.Add(card);
        return panel;
    }

    /// <summary>İnce çerçeveli kutu (tema fırçalarına bağlı kalmadan açık ve koyu temada çalışır).</summary>
    private static Border Boxed(FrameworkElement child) => new()
    {
        BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray) { Opacity = 0.3 },
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(14, 10, 14, 10),
        Child = child,
    };

    /// <summary>Ayar satırı: solda ad ve açıklama, sağda denetim.</summary>
    private static Border Card(string label, string? description, FrameworkElement control)
    {
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        if (description is not null)
        {
            text.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 12 });
        }
        control.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = HorizontalAlignment.Right;
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return Boxed(grid);
    }

    // ================================================================== hakkında

    public static async Task AboutAsync(XamlRoot root, ElementTheme theme)
    {
        var version = typeof(Dialogs).Assembly.GetName().Version;
        string versionText = version is null ? "0.5.0" : $"{version.Major}.{version.Minor}.{version.Build}";

        var header = new Grid { ColumnSpacing = 16 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        try
        {
            header.Children.Add(new Image
            {
                Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/Square44x44Logo.targetsize-256_altform-unplated.png")),
                Width = 76,
                Height = 76,
            });
        }
        catch (Exception ex)
        {
            CrashLog.Write("Logo", ex);
        }
        var titles = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "Zero Data Peek", FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        titles.Children.Add(new TextBlock { Text = Loc.F("Sürüm {0}", versionText), Opacity = 0.7 });
        titles.Children.Add(new TextBlock { Text = Loc.T("Veriye, koda ve belgeye tek pencereden bak."), Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);

        var about = new StackPanel { Spacing = 6 };
        about.Children.Add(new TextBlock
        {
            Text = Loc.T("CSV, Parquet, JSON ve Excel dosyalarını tablo olarak açan, SQL ile sorgulayan; kod dosyalarını renklendiren ve Word belgelerini düzenleyen sekmeli bir uygulama. Açık kaynaklıdır, veri toplamaz ve internete bağlanmaz."),
            TextWrapping = TextWrapping.Wrap,
        });

        var developer = new StackPanel { Spacing = 2 };
        developer.Children.Add(Heading(Loc.T("Geliştirici")));
        developer.Children.Add(new TextBlock { Text = "Ali Apaydın", FontSize = 16 });
        developer.Children.Add(new TextBlock
        {
            Text = Loc.T("Zero ailesi uygulamalarının geliştiricisi."),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            FontSize = 12,
        });
        developer.Children.Add(Link("github.com/aliapaydin", "https://github.com/aliapaydin"));
        developer.Children.Add(Link("sifirgecikme.com", "https://sifirgecikme.com"));

        var project = new StackPanel { Spacing = 2 };
        project.Children.Add(Heading(Loc.T("Proje")));
        project.Children.Add(Link(Loc.T("Kaynak kod (GitHub)"), MainWindow.GitHubUrl));
        project.Children.Add(Link(Loc.T("Hata bildir ya da özellik iste"), MainWindow.GitHubUrl + "/issues"));
        project.Children.Add(Link(Loc.T("Gizlilik politikası"), MainWindow.GitHubUrl + "/blob/main/PRIVACY.md"));
        project.Children.Add(new TextBlock { Text = Loc.T("Lisans: MIT"), Opacity = 0.7, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) });

        var family = new StackPanel { Spacing = 2 };
        family.Children.Add(Heading(Loc.T("Zero ailesi")));
        family.Children.Add(Link(Loc.T("Zero File Commander: çift panelli dosya yöneticisi"), "https://github.com/aliapaydin/zero-file-commander"));
        family.Children.Add(Link(Loc.T("Zero File Extractor: arşiv aracı"), "https://github.com/aliapaydin/Zero-File-Extractor"));

        var credits = new StackPanel { Spacing = 2 };
        credits.Children.Add(Heading(Loc.T("Açık kaynak bileşenler")));
        credits.Children.Add(new TextBlock
        {
            Text = "DuckDB · DuckDB.NET · Monaco Editor · ExcelDataReader · JSZip · SQL Formatter · Marked · Windows App SDK",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            FontSize = 12,
        });

        var columns = new Grid { ColumnSpacing = 12 };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = Boxed(developer);
        var rightPanel = new StackPanel { Spacing = 12 };
        rightPanel.Children.Add(project);
        rightPanel.Children.Add(family);
        var right = Boxed(rightPanel);
        columns.Children.Add(left);
        Grid.SetColumn(right, 1);
        columns.Children.Add(right);

        var panel = new StackPanel { Spacing = 14, Width = 640 };
        panel.Children.Add(header);
        panel.Children.Add(about);
        panel.Children.Add(columns);
        panel.Children.Add(credits);

        var dialog = Create(root, theme, Loc.T("Hakkında"), new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, close: Loc.T("Tamam"));
        Widen(dialog, 740);
        await ShowAsync(dialog);
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 2),
    };

    private static HyperlinkButton Link(string text, string url) => new()
    {
        Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
        NavigateUri = new Uri(url),
        Padding = new Thickness(0, 2, 0, 2),
    };

    // ================================================================== küçük giriş diyalogları

    /// <summary>Tek satırlık metin sorar (ör. yeni dosya adı). Vazgeçilirse null döner.</summary>
    public static async Task<string?> PromptAsync(XamlRoot root, ElementTheme theme, string title, string initial, string confirm)
    {
        var box = new TextBox { Text = initial, MinWidth = 340 };
        box.SelectAll();
        var dialog = Create(root, theme, title, box, confirm, Loc.T("Vazgeç"), ContentDialogButton.Primary);
        box.Loaded += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            int dot = initial.LastIndexOf('.');
            box.Select(0, dot > 0 ? dot : initial.Length); // uzantı seçili gelmesin
        };
        if (await ShowAsync(dialog) != ContentDialogResult.Primary) return null;
        string text = box.Text.Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>CSV okuma seçenekleri. Vazgeçilirse null döner.</summary>
    public static async Task<CsvOptions?> CsvOptionsAsync(XamlRoot root, ElementTheme theme, CsvOptions current)
    {
        string[] delimiters = { "", ",", ";", "\t", "|" };
        var delimiterBox = Combo(new[] { Loc.T("Otomatik algıla"), Loc.T("Virgül (,)"), Loc.T("Noktalı virgül (;)"), Loc.T("Sekme"), Loc.T("Dikey çizgi (|)") },
            Math.Max(0, Array.IndexOf(delimiters, current.Delimiter)));
        string[] decimals = { "", ".", "," };
        var decimalBox = Combo(new[] { Loc.T("Otomatik algıla"), Loc.T("Nokta (1.5)"), Loc.T("Virgül (1,5)") }, Math.Max(0, Array.IndexOf(decimals, current.Decimal)));
        var headerBox = Combo(new[] { Loc.T("Otomatik algıla"), Loc.T("İlk satır başlık"), Loc.T("Başlık satırı yok") }, Math.Clamp(current.Header, 0, 2));
        string[] encodings = { "", "utf-8", "ansi", "utf-16" };
        var encodingBox = Combo(new[] { Loc.T("Otomatik algıla"), "UTF-8", Loc.T("Windows (ANSI, ör. 1254)"), "UTF-16" }, Math.Max(0, Array.IndexOf(encodings, current.Encoding)));
        var textSwitch = Switch(current.AllText);

        var panel = Section(
            Card(Loc.T("Ayraç"), null, delimiterBox),
            Card(Loc.T("Ondalık ayırıcı"), null, decimalBox),
            Card(Loc.T("Başlık satırı"), null, headerBox),
            Card(Loc.T("Kodlama"), null, encodingBox),
            Card(Loc.T("Tüm kolonları metin olarak al"), Loc.T("Baştaki sıfırlar ve uzun kimlikler bozulmaz; sayısal sıralama ve ortalama çalışmaz."), textSwitch));
        panel.Width = 520;

        var dialog = Create(root, theme, Loc.T("CSV okuma seçenekleri"), panel, Loc.T("Yeniden oku"), Loc.T("Vazgeç"), ContentDialogButton.Primary);
        Widen(dialog, 620);
        if (await ShowAsync(dialog) != ContentDialogResult.Primary) return null;
        return new CsvOptions(
            delimiters[Math.Max(0, delimiterBox.SelectedIndex)],
            decimals[Math.Max(0, decimalBox.SelectedIndex)],
            Math.Max(0, headerBox.SelectedIndex),
            encodings[Math.Max(0, encodingBox.SelectedIndex)],
            textSwitch.IsOn);
    }

    /// <summary>Koşullu süzgeç: kolon, koşul ve değer. Vazgeçilirse null döner.</summary>
    public static async Task<DataFilter?> FilterAsync(XamlRoot root, ElementTheme theme, IReadOnlyList<DataColumn> columns, int column, string? value)
    {
        if (columns.Count == 0) return null;
        var columnBox = Combo(columns.Select(c => c.Name), column);
        columnBox.MinWidth = 260;

        // (ad, koşul, tersine mi, kaç değer ister)
        var choices = new (string Label, FilterOp Op, bool Exclude, int Values)[]
        {
            (Loc.T("eşittir"), FilterOp.Equals, false, 1),
            (Loc.T("eşit değildir"), FilterOp.Equals, true, 1),
            (Loc.T("içerir"), FilterOp.Contains, false, 1),
            (Loc.T("içermez"), FilterOp.Contains, true, 1),
            (Loc.T("ile başlar"), FilterOp.StartsWith, false, 1),
            (Loc.T("ile biter"), FilterOp.EndsWith, false, 1),
            (Loc.T("büyüktür (>)"), FilterOp.Greater, false, 1),
            (Loc.T("büyük ya da eşittir (≥)"), FilterOp.GreaterOrEqual, false, 1),
            (Loc.T("küçüktür (<)"), FilterOp.Less, false, 1),
            (Loc.T("küçük ya da eşittir (≤)"), FilterOp.LessOrEqual, false, 1),
            (Loc.T("arasındadır"), FilterOp.Between, false, 2),
            (Loc.T("boştur"), FilterOp.Empty, false, 0),
            (Loc.T("boş değildir"), FilterOp.Empty, true, 0),
        };
        bool numeric = columns[Math.Clamp(column, 0, columns.Count - 1)].IsNumeric;
        var opBox = Combo(choices.Select(c => c.Label), numeric ? 6 : 2);
        opBox.MinWidth = 260;

        var valueBox = new TextBox { Text = value ?? string.Empty, MinWidth = 260, IsSpellCheckEnabled = false };
        var secondBox = new TextBox { MinWidth = 260, IsSpellCheckEnabled = false, PlaceholderText = Loc.T("üst sınır") };
        var valueCard = Card(Loc.T("Değer"), Loc.T("Büyük / küçük harf ayrımı yapılmaz. Tarihleri 2026-01-31 biçiminde yaz."), valueBox);
        var secondCard = Card(Loc.T("İkinci değer"), null, secondBox);

        void Sync()
        {
            int values = choices[Math.Max(0, opBox.SelectedIndex)].Values;
            valueCard.Visibility = values >= 1 ? Visibility.Visible : Visibility.Collapsed;
            secondCard.Visibility = values >= 2 ? Visibility.Visible : Visibility.Collapsed;
            valueBox.PlaceholderText = values >= 2 ? Loc.T("alt sınır") : string.Empty;
        }
        opBox.SelectionChanged += (_, _) => Sync();
        Sync();

        var panel = Section(Card(Loc.T("Kolon"), null, columnBox), Card(Loc.T("Koşul"), null, opBox), valueCard, secondCard);
        panel.Width = 520;
        var dialog = Create(root, theme, Loc.T("Süzgeç ekle"), panel, Loc.T("Süz"), Loc.T("Vazgeç"), ContentDialogButton.Primary);
        Widen(dialog, 620);
        valueBox.Loaded += (_, _) => valueBox.Focus(FocusState.Programmatic);
        if (await ShowAsync(dialog) != ContentDialogResult.Primary) return null;

        var choice = choices[Math.Max(0, opBox.SelectedIndex)];
        string name = columns[Math.Clamp(columnBox.SelectedIndex, 0, columns.Count - 1)].Name;
        string first = valueBox.Text.Trim(), second = secondBox.Text.Trim();
        if (choice.Values >= 1 && first.Length == 0) return null;
        if (choice.Values >= 2 && second.Length == 0) return null;
        return new DataFilter(name, choice.Values == 0 ? null : first, choice.Exclude, choice.Op, choice.Values == 2 ? second : null);
    }

    /// <summary>Karşılaştırılacak iki tarafı seçtirir (açık metin sekmeleri ve "Dosya seç…"). Vazgeçilirse null döner.</summary>
    public static async Task<(int Left, int Right)?> CompareAsync(XamlRoot root, ElementTheme theme, IReadOnlyList<string> names, int left, int right)
    {
        var leftBox = Combo(names, left);
        var rightBox = Combo(names, right);
        leftBox.MinWidth = rightBox.MinWidth = 280;
        var panel = Section(
            Card(Loc.T("Sol (özgün)"), null, leftBox),
            Card(Loc.T("Sağ (değişmiş)"), null, rightBox));
        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("Açık sekmeler o anki içerikleriyle (kaydedilmemiş değişiklikler dahil) karşılaştırılır. Karşılaştırma salt okunurdur."),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            FontSize = 12,
        });
        panel.Width = 520;
        var dialog = Create(root, theme, Loc.T("Dosyaları karşılaştır"), panel, Loc.T("Karşılaştır"), Loc.T("Vazgeç"), ContentDialogButton.Primary);
        Widen(dialog, 620);
        if (await ShowAsync(dialog) != ContentDialogResult.Primary) return null;
        return (Math.Max(0, leftBox.SelectedIndex), Math.Max(0, rightBox.SelectedIndex));
    }

    /// <summary>Süzgecin kısa, okunur hali: "tutar > 100".</summary>
    public static string Describe(DataFilter filter)
    {
        string value = filter.Value is null ? "null" : filter.Value.Length > 24 ? filter.Value[..24] + "…" : filter.Value;
        return filter.Op switch
        {
            FilterOp.Contains => filter.Exclude ? Loc.F("{0} içermez: {1}", filter.Column, value) : Loc.F("{0} içerir: {1}", filter.Column, value),
            FilterOp.StartsWith => Loc.F("{0} ile başlar: {1}", filter.Column, value),
            FilterOp.EndsWith => Loc.F("{0} ile biter: {1}", filter.Column, value),
            FilterOp.Greater => $"{filter.Column} > {value}",
            FilterOp.GreaterOrEqual => $"{filter.Column} ≥ {value}",
            FilterOp.Less => $"{filter.Column} < {value}",
            FilterOp.LessOrEqual => $"{filter.Column} ≤ {value}",
            FilterOp.Between => $"{value} ≤ {filter.Column} ≤ {filter.Value2}",
            FilterOp.Empty => filter.Exclude ? Loc.F("{0}: boş değil", filter.Column) : Loc.F("{0}: boş", filter.Column),
            _ => filter.Exclude ? $"{filter.Column} ≠ {value}" : $"{filter.Column} = {value}",
        };
    }

    private static ComboBox Combo(IEnumerable<string> items, int selected)
    {
        var box = new ComboBox { MinWidth = 200 };
        foreach (var item in items) box.Items.Add(item);
        box.SelectedIndex = Math.Clamp(selected, 0, Math.Max(0, box.Items.Count - 1));
        return box;
    }

    private static ToggleSwitch Switch(bool on) => new()
    {
        IsOn = on,
        OnContent = Loc.T("Açık"),
        OffContent = Loc.T("Kapalı"),
        MinWidth = 0,
    };

}
