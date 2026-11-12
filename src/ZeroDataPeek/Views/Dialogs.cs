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

    /// <summary>Ayarlar. Kaydedilirse true döner; çağıran taraf ayarları yeniden uygular.</summary>
    public static async Task<bool> SettingsAsync(XamlRoot root, ElementTheme theme, AppSettings settings)
    {
        var themeBox = Combo(new[] { Loc.T("Windows ayarını kullan"), Loc.T("Açık tema"), Loc.T("Koyu tema") }, (int)settings.Theme);
        var languageBox = Combo(new[] { Loc.T("Windows diline göre"), "Türkçe", "English" },
            settings.UiLanguage switch { "tr" => 1, "en" => 2, _ => 0 });
        var tabsBox = Combo(new[] { Loc.T("Üstte (yatay)"), Loc.T("Solda (dikey)") }, settings.VerticalTabs ? 1 : 0);

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
            MinWidth = 160,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var wrapSwitch = Switch(settings.WordWrap);
        var statusSwitch = Switch(settings.ShowStatusBar);
        var sessionSwitch = Switch(settings.RestoreSession);

        var panel = new StackPanel { Spacing = 12, MinWidth = 380 };
        panel.Children.Add(Row(Loc.T("Tema"), themeBox));
        panel.Children.Add(Row(Loc.T("Dil"), languageBox));
        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("Dil değişikliği uygulama yeniden başlatılınca geçerli olur."),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.65,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        });
        panel.Children.Add(Row(Loc.T("Sekmeler"), tabsBox));
        panel.Children.Add(Row(Loc.T("Yazı tipi"), fontBox));
        panel.Children.Add(Row(Loc.T("Yazı boyutu"), sizeBox));
        panel.Children.Add(Row(Loc.T("Sözcük kaydırma"), wrapSwitch));
        panel.Children.Add(Row(Loc.T("Durum çubuğu"), statusSwitch));
        panel.Children.Add(Row(Loc.T("Açılışta son oturumu geri yükle"), sessionSwitch));

        var dialog = Create(root, theme, Loc.T("Ayarlar"), new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            Loc.T("Kaydet"), Loc.T("Vazgeç"), ContentDialogButton.Primary);
        if (await ShowAsync(dialog) != ContentDialogResult.Primary) return false;

        settings.Theme = (AppTheme)Math.Max(0, themeBox.SelectedIndex);
        settings.UiLanguage = languageBox.SelectedIndex switch { 1 => "tr", 2 => "en", _ => string.Empty };
        settings.VerticalTabs = tabsBox.SelectedIndex == 1;
        if (fontBox.SelectedItem is string font) settings.FontFamily = font;
        if (!double.IsNaN(sizeBox.Value)) settings.FontSize = Math.Clamp(sizeBox.Value, 8, 48);
        settings.WordWrap = wrapSwitch.IsOn;
        settings.ShowStatusBar = statusSwitch.IsOn;
        settings.RestoreSession = sessionSwitch.IsOn;
        return true;
    }

    public static async Task AboutAsync(XamlRoot root, ElementTheme theme)
    {
        var version = typeof(Dialogs).Assembly.GetName().Version;
        var panel = new StackPanel { Spacing = 8, MinWidth = 340 };
        panel.Children.Add(new TextBlock
        {
            Text = Loc.F("Sürüm {0}", version is null ? "0.1.0" : $"{version.Major}.{version.Minor}.{version.Build}"),
        });
        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("Veri, kod ve belge dosyalarını tek pencerede açan sekmeli görüntüleyici ve düzenleyici. Zero File Commander ve Zero File Extractor ile aynı ailenin üyesi."),
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("Açık kaynak (MIT). Veri toplamaz, internete bağlanmaz."),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.65,
        });
        panel.Children.Add(new HyperlinkButton
        {
            Content = "github.com/aliapaydin/zero-data-peek",
            NavigateUri = new Uri(MainWindow.GitHubUrl),
            Padding = new Thickness(0),
        });
        await ShowAsync(Create(root, theme, "Zero Data Peek", panel, close: Loc.T("Tamam")));
    }

    private static ComboBox Combo(IEnumerable<string> items, int selected)
    {
        var box = new ComboBox { MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left };
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

    private static StackPanel Row(string label, FrameworkElement control)
    {
        var row = new StackPanel { Spacing = 4 };
        row.Children.Add(new TextBlock { Text = label });
        row.Children.Add(control);
        return row;
    }
}
