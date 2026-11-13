using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using ZeroDataPeek.Services;

namespace ZeroDataPeek.Views;

/// <summary>
/// Belge modu: WebView2 içinde çalışan zengin metin düzenleyici (Assets/docedit). Sayfa, araç çubuğunu ve
/// A4 görünümlü düzenleme alanını kendisi çizer; DOCX okuma ve yazma da sayfanın içindedir (docx.js).
/// Bu sınıf köprüdür: dosyayı sayfaya verir, kaydederken içeriği geri alır, PDF ve yazdırmayı WebView2'ye yaptırır.
/// </summary>
public sealed partial class RichDocumentView : Grid
{
    private const string HostName = "zdpdoc.local";
    private const int MinZoom = 50, MaxZoom = 300;

    private readonly WebView2 _web = new();
    private readonly IReadOnlyList<EditorShortcut> _shortcuts;
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _requests = new();
    private AppSettings? _settings;
    private int _nextRequest;
    private int _zoom = 100;
    private long _words, _characters;

    public RichDocumentView(IReadOnlyList<EditorShortcut> shortcuts)
    {
        _shortcuts = shortcuts;
        Children.Add(_web);
        ActualThemeChanged += (_, _) => SendOptions();
        Loaded += OnFirstLoaded;
    }

    /// <summary>Kullanıcı belgeyi değiştirdi.</summary>
    public event EventHandler? Modified;

    /// <summary>Sözcük sayısı ya da yakınlaştırma değişti (durum çubuğu).</summary>
    public event EventHandler? StatusChanged;

    /// <summary>Düzenleyici odaktayken bir uygulama kısayoluna basıldı (komut kimliği).</summary>
    public event EventHandler<string>? ShortcutPressed;

    public int Zoom => _zoom;

    /// <summary>Açılan DOCX'te düzenleyicinin taşıyamadığı öğeler (üstbilgi, dipnot…); üzerine kaydetmeden önce uyarı için.</summary>
    public IReadOnlyList<string> Losses { get; private set; } = Array.Empty<string>();

    public string StatusText => Loc.F("{0} sözcük · {1} karakter",
        _words.ToString("N0", CultureInfo.CurrentCulture), _characters.ToString("N0", CultureInfo.CurrentCulture));

    /// <summary>Bu uzantı belge modunda açılır mı.</summary>
    public static bool Supports(string? path)
    {
        try
        {
            return string.Equals(Path.GetExtension(path), ".docx", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    // ================================================================== başlatma

    private void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            string folder = Path.Combine(AppContext.BaseDirectory, "Assets", "docedit");
            if (!File.Exists(Path.Combine(folder, "editor.html"))) throw new FileNotFoundException(folder);

            await _web.EnsureCoreWebView2Async();
            var core = _web.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping(HostName, folder, CoreWebView2HostResourceAccessKind.Allow);
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.NavigationStarting += (_, e) =>
            {
                // Belgedeki bağlantılar düzenleyiciyi başka sayfaya götürmesin.
                if (!e.Uri.StartsWith("https://" + HostName + "/", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
            };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.WebMessageReceived += Core_WebMessageReceived;
            core.Navigate("https://" + HostName + "/editor.html");
        }
        catch (Exception ex)
        {
            CrashLog.Write("Belge düzenleyici", ex);
            Fail(ex.Message);
        }
    }

    private void Fail(string reason)
    {
        _ready.TrySetResult(false);
        Children.Clear();
        Children.Add(new PlaceholderView("", Loc.T("Belge düzenleyici açılamadı"), reason));
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
                    Post(new { type = "shortcuts", list = _shortcuts.Select(s => new { id = s.Id, key = s.Key, ctrl = s.Ctrl, shift = s.Shift, alt = s.Alt }).ToArray() });
                    _ready.TrySetResult(true);
                    SendOptions();
                    break;
                case "loaded":
                case "exported":
                case "failed":
                    if (root.TryGetProperty("requestId", out var id) && id.TryGetInt32(out int request) && _requests.Remove(request, out var pending))
                    {
                        pending.TrySetResult(root.Clone());
                    }
                    break;
                case "changed":
                    Modified?.Invoke(this, EventArgs.Empty);
                    break;
                case "stats":
                    _words = Number(root, "words");
                    _characters = Number(root, "chars");
                    StatusChanged?.Invoke(this, EventArgs.Empty);
                    break;
                case "zoom":
                    ZoomBy((int)Number(root, "delta"));
                    break;
                case "shortcut":
                    if (root.TryGetProperty("id", out var command) && command.GetString() is { } commandId) ShortcutPressed?.Invoke(this, commandId);
                    break;
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Belge iletisi", ex);
        }
    }

    private static long Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) ? number : 0;

    /// <summary>Düzenleyici sayfası hazır olana kadar bekler; bir dakikada hazır olmazsa false döner.</summary>
    private async Task<bool> ReadyAsync()
    {
        var finished = await Task.WhenAny(_ready.Task, Task.Delay(TimeSpan.FromSeconds(60)));
        return finished == _ready.Task && _ready.Task.Result;
    }

    /// <summary>Sayfaya istek gönderir ve yanıtını (loaded / exported / failed) bekler.</summary>
    private async Task<JsonElement> RequestAsync(Func<int, object> message)
    {
        if (!await ReadyAsync()) throw new InvalidOperationException(Loc.T("Belge düzenleyici açılamadı"));
        int id = ++_nextRequest;
        var pending = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _requests[id] = pending;
        Post(message(id));
        var result = await pending.Task;
        if (result.TryGetProperty("type", out var type) && type.GetString() == "failed")
        {
            throw new InvalidDataException(result.TryGetProperty("message", out var text) ? text.GetString() : null);
        }
        return result;
    }

    // ================================================================== açma ve kaydetme

    /// <summary>Boş belge ya da dosyadan içerik yükler. Hata fırlatabilir (bozuk dosya, düzenleyici açılamadı).</summary>
    public async Task LoadAsync(string? path)
    {
        if (path is null)
        {
            await RequestAsync(id => new { type = "load", format = "empty", data = string.Empty, requestId = id });
            return;
        }

        byte[] bytes = await Task.Run(() => File.ReadAllBytes(path));
        string data = Convert.ToBase64String(bytes);
        var result = await RequestAsync(id => new { type = "load", format = "docx", data, requestId = id });
        if (result.TryGetProperty("losses", out var losses) && losses.ValueKind == JsonValueKind.Array)
        {
            Losses = losses.EnumerateArray().Select(l => l.GetString() ?? string.Empty).Where(l => l.Length > 0).ToList();
        }
    }

    /// <summary>Belgeyi uzantıya göre yazar: .docx, .html / .htm, .txt ya da .pdf.</summary>
    public async Task SaveAsync(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (!await ReadyAsync()) throw new InvalidOperationException(Loc.T("Belge düzenleyici açılamadı"));

        if (extension == ".pdf")
        {
            bool ok = await _web.CoreWebView2.PrintToPdfAsync(path, null);
            if (!ok) throw new IOException(Loc.T("PDF oluşturulamadı."));
            return;
        }

        byte[] bytes;
        if (extension is ".html" or ".htm")
        {
            string title = JsonSerializer.Serialize(Path.GetFileNameWithoutExtension(path));
            bytes = new UTF8Encoding(false).GetBytes(await ScriptStringAsync("zdoc.html(" + title + ")"));
        }
        else if (extension == ".txt")
        {
            string text = (await ScriptStringAsync("zdoc.text()")).Replace("\r\n", "\n").Replace("\n", "\r\n");
            bytes = new UTF8Encoding(true).GetBytes(text);
        }
        else
        {
            var result = await RequestAsync(id => new { type = "export", format = "docx", requestId = id });
            bytes = Convert.FromBase64String(result.GetProperty("data").GetString() ?? string.Empty);
            if (bytes.Length == 0) throw new InvalidDataException(Loc.T("Belge dışa aktarılamadı."));
        }

        await Task.Run(() =>
        {
            string temp = path + ".zdp-tmp";
            try
            {
                File.WriteAllBytes(temp, bytes);
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                try
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
                catch (Exception)
                {
                    // geçici dosya kalabilir
                }
                File.WriteAllBytes(path, bytes);
            }
        });
    }

    /// <summary>Kaydedildikten sonra: belge artık bizim yazdığımız biçimde, kayıp uyarısına gerek kalmadı.</summary>
    public void ClearLosses() => Losses = Array.Empty<string>();

    public void Print()
    {
        try
        {
            _web.CoreWebView2?.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Yazdırma", ex);
        }
    }

    private async Task<string> ScriptStringAsync(string script)
    {
        string json = await _web.ExecuteScriptAsync(script);
        return JsonSerializer.Deserialize<string>(json) ?? string.Empty;
    }

    // ================================================================== ayarlar ve komutlar

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        SendOptions();
    }

    public void ZoomBy(int percent) => SetZoom(_zoom + percent);

    public void SetZoom(int percent)
    {
        int clamped = Math.Clamp(percent, MinZoom, MaxZoom);
        if (clamped == _zoom) return;
        _zoom = clamped;
        SendOptions();
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SendOptions()
    {
        if (!_ready.Task.IsCompletedSuccessfully || !_ready.Task.Result) return;
        Post(new
        {
            type = "options",
            theme = ActualTheme == ElementTheme.Dark ? "dark" : "light",
            zoom = _zoom,
            lang = Loc.IsEnglish ? "en" : "tr",
        });
    }

    public void FocusEditor()
    {
        _web.Focus(FocusState.Programmatic);
        Command("focus");
    }

    /// <summary>Düzenleyici komutu: undo, redo, selectAll, bold…</summary>
    public void Command(string id)
    {
        if (!_ready.Task.IsCompletedSuccessfully || !_ready.Task.Result) return;
        try
        {
            _ = _web.ExecuteScriptAsync("zdoc.command(" + JsonSerializer.Serialize(id) + ")");
        }
        catch (Exception ex)
        {
            CrashLog.Write("Belge komutu", ex);
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
            CrashLog.Write("Belge iletisi", ex);
        }
    }
}
