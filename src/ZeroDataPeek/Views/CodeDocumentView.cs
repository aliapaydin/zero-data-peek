using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using ZeroDataPeek.Services;

namespace ZeroDataPeek.Views;

/// <summary>Düzenleyici odaktayken de çalışması gereken bir uygulama kısayolu (tuş kodu Windows sanal tuş kodudur).</summary>
public sealed record EditorShortcut(string Id, int Key, bool Ctrl, bool Shift, bool Alt);

/// <summary>
/// Kod düzenleyici: WebView2 içinde Monaco (VS Code'un düzenleyicisi). Sözdizimi renklendirme, bul/değiştir,
/// kod katlama ve çoklu imleç oradan gelir. Sayfa paketle birlikte gelen yerel dosyalardan yüklenir
/// (Assets/monaco); internete bağlanmaz. WebView2 kullanılamıyorsa ya da sayfa açılmazsa aynı içerik
/// düz metin kutusunda (<see cref="TextDocumentView"/>) gösterilir.
/// </summary>
public sealed partial class CodeDocumentView : Grid, ITextEditor
{
    private const string HostName = "zdp.local";
    private const int MinZoom = 50, MaxZoom = 400;

    private readonly WebView2 _web = new();
    private readonly IReadOnlyList<EditorShortcut> _shortcuts;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _readyTimer;
    private TextDocumentView? _fallback;
    private bool _ready;
    private string _text = string.Empty;
    private string? _fileName;
    private AppSettings? _settings;
    private int _zoom = 100;
    private CaretInfo _caret = new(1, 1, 0, 0);

    public CodeDocumentView(IReadOnlyList<EditorShortcut> shortcuts)
    {
        _shortcuts = shortcuts;
        _web.HorizontalAlignment = HorizontalAlignment.Stretch;
        _web.VerticalAlignment = VerticalAlignment.Stretch;
        Children.Add(_web);

        _readyTimer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        _readyTimer.Interval = TimeSpan.FromSeconds(20);
        _readyTimer.IsRepeating = false;
        _readyTimer.Tick += (_, _) =>
        {
            if (_ready) return;
            // Arka plandaki (görünmeyen) sekmede sayfa gecikebilir: görünür olana kadar beklemeyi sürdür.
            if (ActualWidth < 1 || Visibility != Visibility.Visible) _readyTimer.Start();
            else UseFallback("Düzenleyici sayfası 20 saniyede hazır olmadı.");
        };

        ActualThemeChanged += (_, _) => SendOptions();
        Loaded += OnFirstLoaded;
    }

    private void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        _ = InitializeAsync();
    }

    public event EventHandler? Modified;
    public event EventHandler? CaretMoved;
    public event EventHandler? ZoomChanged;

    /// <summary>Düzenleyici odaktayken bir uygulama kısayoluna basıldı (komut kimliği).</summary>
    public event EventHandler<string>? ShortcutPressed;

    public EncodingChoice Encoding { get; set; } = TextFileIO.Utf8;

    public string Eol { get; set; } = "\r\n";

    public int Zoom => _fallback?.Zoom ?? _zoom;

    // ================================================================== başlatma

    private async Task InitializeAsync()
    {
        try
        {
            string folder = Path.Combine(AppContext.BaseDirectory, "Assets", "monaco");
            if (!File.Exists(Path.Combine(folder, "editor.html")))
            {
                UseFallback("Düzenleyici dosyaları bulunamadı: " + folder);
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

            // Sayfa yalnızca kendi yerel dosyalarını gösterir; dışarıya gezinme ve yeni pencere yok.
            core.NavigationStarting += (_, e) =>
            {
                if (!e.Uri.StartsWith("https://" + HostName + "/", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
            };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.WebMessageReceived += Core_WebMessageReceived;

            _readyTimer.Start();
            core.Navigate("https://" + HostName + "/editor.html");
        }
        catch (Exception ex)
        {
            CrashLog.Write("Kod düzenleyici", ex);
            UseFallback(ex.Message);
        }
    }

    private void Core_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using var document = JsonDocument.Parse(args.WebMessageAsJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var typeElement)) return;

            switch (typeElement.GetString())
            {
                case "ready":
                    _ready = true;
                    _readyTimer.Stop();
                    Post(new { type = "shortcuts", list = _shortcuts.Select(s => new { id = s.Id, key = s.Key, ctrl = s.Ctrl, shift = s.Shift, alt = s.Alt }).ToArray() });
                    SendOptions();
                    Post(new { type = "load", text = _text, fileName = _fileName ?? string.Empty });
                    break;
                case "failed":
                    UseFallback(root.TryGetProperty("message", out var message) ? message.GetString() ?? string.Empty : string.Empty);
                    break;
                case "changed":
                    Modified?.Invoke(this, EventArgs.Empty);
                    break;
                case "cursor":
                    _caret = new CaretInfo(Int(root, "line", 1), Int(root, "column", 1), Int(root, "length", 0), Int(root, "selected", 0));
                    CaretMoved?.Invoke(this, EventArgs.Empty);
                    break;
                case "zoom":
                    ZoomBy(Int(root, "delta", 0));
                    break;
                case "shortcut":
                    if (root.TryGetProperty("id", out var id) && id.GetString() is { } command) ShortcutPressed?.Invoke(this, command);
                    break;
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Düzenleyici iletisi", ex);
        }
    }

    private static int Int(JsonElement root, string name, int fallback) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : fallback;

    /// <summary>Monaco kullanılamıyor: içeriği düz metin kutusuna taşı.</summary>
    private void UseFallback(string reason)
    {
        if (_fallback is not null) return;
        CrashLog.Write("Kod düzenleyici yerine düz metin kutusu kullanılıyor: " + reason, null);
        _readyTimer.Stop();
        _ready = false;

        var fallback = new TextDocumentView();
        if (_settings is not null) fallback.ApplySettings(_settings);
        fallback.Load(new LoadedText(_text, Encoding, Eol));
        fallback.Modified += (_, _) => Modified?.Invoke(this, EventArgs.Empty);
        fallback.CaretMoved += (_, _) => CaretMoved?.Invoke(this, EventArgs.Empty);
        fallback.ZoomChanged += (_, _) => ZoomChanged?.Invoke(this, EventArgs.Empty);
        _fallback = fallback;

        Children.Clear();
        Children.Add(fallback);
        try
        {
            _web.Close();
        }
        catch (Exception)
        {
            // zaten kapalı
        }
    }

    // ================================================================== içerik

    public void Load(LoadedText loaded)
    {
        Encoding = loaded.Encoding;
        Eol = loaded.Eol;
        _text = loaded.Text;
        if (_fallback is not null) _fallback.Load(loaded);
        else if (_ready) Post(new { type = "load", text = _text, fileName = _fileName ?? string.Empty });
    }

    public void SetFileName(string? fileName)
    {
        _fileName = fileName;
        if (_ready && _fallback is null) Run("zdp.setFileName(" + JsonSerializer.Serialize(fileName ?? string.Empty) + ")");
    }

    public async Task<string> GetTextAsync()
    {
        if (_fallback is not null) return await _fallback.GetTextAsync();
        if (!_ready) return _text;
        string json = await _web.ExecuteScriptAsync("zdp.text()");
        return JsonSerializer.Deserialize<string>(json) ?? string.Empty;
    }

    public CaretInfo GetCaret() => _fallback?.GetCaret() ?? _caret;

    // ================================================================== ayarlar

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _fallback?.ApplySettings(settings);
        SendOptions();
    }

    public void ZoomBy(int percent) => SetZoom(Zoom + percent);

    public void SetZoom(int percent)
    {
        if (_fallback is not null)
        {
            _fallback.SetZoom(percent);
            return;
        }
        int clamped = Math.Clamp(percent, MinZoom, MaxZoom);
        if (clamped == _zoom) return;
        _zoom = clamped;
        SendOptions();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SendOptions()
    {
        if (!_ready || _fallback is not null) return;
        double size = Math.Clamp(_settings?.FontSize ?? AppSettings.DefaultFontSize, 8, 48) * Density.EditorScale * _zoom / 100.0;
        Post(new
        {
            type = "options",
            theme = ActualTheme == ElementTheme.Dark ? "dark" : "light",
            fontFamily = _settings?.FontFamily ?? AppSettings.DefaultFontFamily,
            fontSize = Math.Max(6, Math.Round(size, 1)),
            wordWrap = _settings?.WordWrap ?? false,
            minimap = _settings?.ShowMinimap ?? true,
            compact = Density.Compact,
        });
    }

    // ================================================================== komutlar

    public void FocusEditor()
    {
        if (_fallback is not null)
        {
            _fallback.FocusEditor();
            return;
        }
        _web.Focus(FocusState.Programmatic);
        Command("focus");
    }

    public void Undo()
    {
        if (_fallback is not null) _fallback.Undo();
        else Command("undo");
    }

    public void Redo()
    {
        if (_fallback is not null) _fallback.Redo();
        else Command("redo");
    }

    public void SelectAll()
    {
        if (_fallback is not null) _fallback.SelectAll();
        else Command("selectAll");
    }

    public void Find() => Command("find");

    public void Replace() => Command("replace");

    public void GoToLine() => Command("gotoLine");

    public async Task<string> FormatAsync()
    {
        if (_fallback is not null || !_ready) return "unsupported";
        try
        {
            string json = await _web.ExecuteScriptAsync("zdp.format()");
            return JsonSerializer.Deserialize<string>(json) ?? "unsupported";
        }
        catch (Exception ex)
        {
            return "error: " + ex.Message;
        }
    }

    public void TogglePreview()
    {
        if (_ready && _fallback is null) Run("zdp.preview()");
    }

    public void Reveal(int line)
    {
        if (_ready && _fallback is null) Run("zdp.reveal(" + line.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", 1)");
    }

    public void Copy()
    {
        if (_fallback is not null) _fallback.Copy();
        else _ = CopySelectionAsync(cut: false);
    }

    public void Cut()
    {
        if (_fallback is not null) _fallback.Cut();
        else _ = CopySelectionAsync(cut: true);
    }

    public void Paste()
    {
        if (_fallback is not null) _fallback.Paste();
        else _ = PasteAsync();
    }

    private async Task CopySelectionAsync(bool cut)
    {
        if (!_ready) return;
        try
        {
            string json = await _web.ExecuteScriptAsync("zdp.selectedText()");
            string text = JsonSerializer.Deserialize<string>(json) ?? string.Empty;
            if (text.Length == 0) return;
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            if (cut) Command("deleteSelection");
        }
        catch (Exception ex)
        {
            CrashLog.Write("Pano", ex);
        }
    }

    private async Task PasteAsync()
    {
        if (!_ready) return;
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text)) return;
            string text = await content.GetTextAsync();
            Post(new { type = "insert", text });
            Command("focus");
        }
        catch (Exception ex)
        {
            CrashLog.Write("Pano", ex);
        }
    }

    private void Command(string id)
    {
        if (_ready && _fallback is null) Run("zdp.command(" + JsonSerializer.Serialize(id) + ")");
    }

    private void Run(string script)
    {
        try
        {
            _ = _web.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Düzenleyici betiği", ex);
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
            CrashLog.Write("Düzenleyici iletisi", ex);
        }
    }
}
