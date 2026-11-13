using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using ZeroDataPeek.Services;

namespace ZeroDataPeek.Views;

/// <summary>
/// Dosya karşılaştırma sekmesi: iki metni Monaco'nun fark düzenleyicisinde yan yana (ya da satır içi) gösterir.
/// Salt okunurdur; Assets/monaco/diff.html sayfasını kullanır (kod düzenleyiciyle aynı yerel dosyalar, internet yok).
/// </summary>
public sealed partial class DiffDocumentView : Grid
{
    private const string HostName = "zdp.local";

    private readonly WebView2 _web = new();
    private readonly IReadOnlyList<EditorShortcut> _shortcuts;
    private readonly TextBlock _leftName = new(), _rightName = new(), _message = new();
    private readonly Grid _header = new();
    private AppSettings? _settings;
    private bool _ready, _sideBySide = true, _ignoreSpace;
    private string _original = string.Empty, _modified = string.Empty, _originalName = string.Empty, _modifiedName = string.Empty;
    private int _changes = -1;

    public DiffDocumentView(IReadOnlyList<EditorShortcut> shortcuts)
    {
        _shortcuts = shortcuts;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        BuildHeader();
        Children.Add(_header);

        SetRow(_web, 1);
        Children.Add(_web);

        _message.HorizontalAlignment = HorizontalAlignment.Center;
        _message.VerticalAlignment = VerticalAlignment.Center;
        _message.TextWrapping = TextWrapping.Wrap;
        _message.Opacity = 0.7;
        _message.Visibility = Visibility.Collapsed;
        SetRow(_message, 1);
        Children.Add(_message);

        ActualThemeChanged += (_, _) => SendOptions();
        Loaded += OnFirstLoaded;
    }

    /// <summary>Fark sayısı değişti (durum çubuğu için).</summary>
    public event EventHandler? StatusChanged;

    /// <summary>Sayfa odaktayken bir uygulama kısayoluna basıldı (komut kimliği).</summary>
    public event EventHandler<string>? ShortcutPressed;

    public string StatusText => _changes < 0 ? string.Empty
        : _changes == 0 ? Loc.T("Fark yok: iki metin aynı")
        : Loc.F("{0} farklı bölüm", _changes);

    /// <summary>Karşılaştırılacak iki metni verir (sol: özgün, sağ: değişmiş).</summary>
    public void SetContent(string original, string originalName, string modified, string modifiedName)
    {
        (_original, _originalName, _modified, _modifiedName) = (original, originalName, modified, modifiedName);
        _leftName.Text = originalName;
        _rightName.Text = modifiedName;
        SendContent();
    }

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _header.Padding = Density.Compact ? new Thickness(8, 2, 6, 2) : new Thickness(12, 6, 8, 6);
        SendOptions();
    }

    public void FocusEditor()
    {
        if (!_ready) return;
        _web.Focus(FocusState.Programmatic);
        Run("zdp.focus()");
    }

    private void BuildHeader()
    {
        _header.Padding = new Thickness(12, 6, 8, 6);
        _header.ColumnSpacing = 8;
        _header.BorderThickness = new Thickness(0, 0, 0, 1);
        if (Application.Current.Resources.TryGetValue("DividerStrokeColorDefaultBrush", out var brush) && brush is Microsoft.UI.Xaml.Media.Brush divider)
        {
            _header.BorderBrush = divider;
        }
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        foreach (var name in new[] { _leftName, _rightName })
        {
            name.VerticalAlignment = VerticalAlignment.Center;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        }
        _header.Children.Add(_leftName);
        var arrow = new FontIcon { Glyph = "", FontSize = 12, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center };
        SetColumn(arrow, 1);
        _header.Children.Add(arrow);
        SetColumn(_rightName, 2);
        _header.Children.Add(_rightName);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        buttons.Children.Add(ToolButton("", Loc.T("Önceki fark"), () => Run("zdp.previous()")));
        buttons.Children.Add(ToolButton("", Loc.T("Sonraki fark"), () => Run("zdp.next()")));
        buttons.Children.Add(ToolButton("", Loc.T("Yan yana ya da satır içi göster"), () =>
        {
            _sideBySide = !_sideBySide;
            Run("zdp.sideBySide(" + (_sideBySide ? "true" : "false") + ")");
        }));
        buttons.Children.Add(ToolButton("", Loc.T("Satır başı ve sonundaki boşluk farklarını yok say"), () =>
        {
            _ignoreSpace = !_ignoreSpace;
            Run("zdp.ignoreWhitespace(" + (_ignoreSpace ? "true" : "false") + ")");
        }));
        buttons.Children.Add(ToolButton("", Loc.T("Sol ve sağı değiştir"), () => SetContent(_modified, _modifiedName, _original, _originalName)));
        SetColumn(buttons, 3);
        _header.Children.Add(buttons);
    }

    private static Button ToolButton(string glyph, string tip, Action action)
    {
        var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 13 } };
        if (Application.Current.Resources.TryGetValue("SubtleButton", out var style) && style is Style subtle) button.Style = subtle;
        ToolTipService.SetToolTip(button, tip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => action();
        return button;
    }

    private void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            string folder = Path.Combine(AppContext.BaseDirectory, "Assets", "monaco");
            if (!File.Exists(Path.Combine(folder, "diff.html")))
            {
                Fail(Loc.T("Karşılaştırma sayfası bulunamadı."));
                return;
            }
            await _web.EnsureCoreWebView2Async();
            var core = _web.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping(HostName, folder, CoreWebView2HostResourceAccessKind.Allow);
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.NavigationStarting += (_, e) =>
            {
                if (!e.Uri.StartsWith("https://" + HostName + "/", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
            };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.WebMessageReceived += Core_WebMessageReceived;
            core.Navigate("https://" + HostName + "/diff.html");
        }
        catch (Exception ex)
        {
            CrashLog.Write("Karşılaştırma", ex);
            Fail(ex.Message);
        }
    }

    private void Fail(string reason)
    {
        _web.Visibility = Visibility.Collapsed;
        _message.Text = Loc.T("Karşılaştırma görünümü açılamadı.") + "\n" + reason;
        _message.Visibility = Visibility.Visible;
    }

    private void Core_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type)) return;
            switch (type.GetString())
            {
                case "ready":
                    _ready = true;
                    Post(new { type = "shortcuts", list = _shortcuts.Select(s => new { id = s.Id, key = s.Key, ctrl = s.Ctrl, shift = s.Shift, alt = s.Alt }).ToArray() });
                    SendOptions();
                    SendContent();
                    break;
                case "failed":
                    Fail(root.TryGetProperty("message", out var message) ? message.GetString() ?? string.Empty : string.Empty);
                    break;
                case "stats":
                    _changes = root.TryGetProperty("changes", out var changes) && changes.TryGetInt32(out int count) ? count : -1;
                    StatusChanged?.Invoke(this, EventArgs.Empty);
                    break;
                case "shortcut":
                    if (root.TryGetProperty("id", out var id) && id.GetString() is { } command) ShortcutPressed?.Invoke(this, command);
                    break;
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Karşılaştırma iletisi", ex);
        }
    }

    private void SendContent()
    {
        if (!_ready) return;
        Post(new { type = "load", original = _original, modified = _modified, originalName = _originalName, modifiedName = _modifiedName });
    }

    private void SendOptions()
    {
        if (!_ready) return;
        double size = Math.Clamp(_settings?.FontSize ?? AppSettings.DefaultFontSize, 8, 48) * Density.EditorScale;
        Post(new
        {
            type = "options",
            theme = ActualTheme == ElementTheme.Dark ? "dark" : "light",
            fontFamily = _settings?.FontFamily ?? AppSettings.DefaultFontFamily,
            fontSize = Math.Max(6, Math.Round(size, 1)),
            wordWrap = _settings?.WordWrap ?? false,
            compact = Density.Compact,
        });
    }

    private void Run(string script)
    {
        if (!_ready) return;
        try
        {
            _ = _web.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Karşılaştırma betiği", ex);
        }
    }

    private void Post(object message)
    {
        try
        {
            _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message));
        }
        catch (Exception ex)
        {
            CrashLog.Write("Karşılaştırma iletisi", ex);
        }
    }
}
