using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;
using ZeroDataPeek.Services;

namespace ZeroDataPeek.Views;

/// <summary>
/// Düz metin kutusu. Kod düzenleyici (WebView2 + Monaco) açılamadığında yedek olarak kullanılır;
/// sözdizimi renklendirme ve bul/değiştir yoktur.
/// </summary>
public sealed partial class TextDocumentView : Grid, ITextEditor
{
    private const int MinZoom = 50, MaxZoom = 400;

    private readonly TextBox _box;
    private bool _loading;
    private string? _loadEcho;
    private double _baseFontSize = AppSettings.DefaultFontSize;
    private int _zoom = 100;

    public TextDocumentView()
    {
        _box = new TextBox
        {
            AcceptsReturn = true,
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = false,
            TextWrapping = TextWrapping.NoWrap,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(14, 10, 14, 10),
            CornerRadius = new CornerRadius(8),
            AllowDrop = false, // dosya bırakma pencereye ulaşsın
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_box, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_box, ScrollBarVisibility.Auto);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_box, Loc.T("Düzenleyici"));

        // Kart yüzeyin üstünde çerçevesiz dursun: metin kutusunun kendi zemin ve alt çizgisini kaldır.
        var transparent = new SolidColorBrush(Colors.Transparent);
        foreach (var key in new[]
        {
            "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
            "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused",
        })
        {
            _box.Resources[key] = transparent;
        }
        _box.Resources["TextControlBorderThemeThickness"] = new Thickness(0);
        _box.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(0);

        // TextChanged olayı gecikmeli gelir; yükleme sırasındaki değişikliği ayırt etmek için eşzamanlı TextChanging kullanılır.
        _box.TextChanging += (_, e) =>
        {
            if (!e.IsContentChanging || _loading) return;
            if (_loadEcho is not null)
            {
                // Yüklemenin kendi olayı sonradan geldiyse (metin yüklenenle aynı) değişiklik sayma.
                string echo = _loadEcho;
                _loadEcho = null;
                if (NormalizeBreaks(_box.Text) == echo) return;
            }
            Modified?.Invoke(this, EventArgs.Empty);
        };
        _box.TextChanged += (_, _) => CaretMoved?.Invoke(this, EventArgs.Empty);
        _box.SelectionChanged += (_, _) => CaretMoved?.Invoke(this, EventArgs.Empty);
        _box.PreviewKeyDown += Box_PreviewKeyDown;
        _box.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(Box_PointerWheelChanged), true);

        Children.Add(_box);
    }

    /// <summary>Kullanıcı metni değiştirdi.</summary>
    public event EventHandler? Modified;

    /// <summary>İmleç ya da seçim değişti.</summary>
    public event EventHandler? CaretMoved;

    /// <summary>Yakınlaştırma değişti.</summary>
    public event EventHandler? ZoomChanged;

    /// <summary>Kaydederken kullanılacak kodlama.</summary>
    public EncodingChoice Encoding { get; set; } = TextFileIO.Utf8;

    /// <summary>Kaydederken kullanılacak satır sonu: "\r\n", "\n" ya da "\r".</summary>
    public string Eol { get; set; } = "\r\n";

    public int Zoom => _zoom;

    public bool CanUndo => _box.CanUndo;
    public bool CanRedo => _box.CanRedo;

    /// <summary>Dosyadan okunan içeriği yükler; geri alma geçmişini temizler.</summary>
    public void Load(LoadedText loaded)
    {
        _loading = true;
        _loadEcho = NormalizeBreaks(loaded.Text);
        try
        {
            _box.Text = loaded.Text;
            _box.ClearUndoRedoHistory();
            _box.SelectionStart = 0;
            _box.SelectionLength = 0;
        }
        finally
        {
            _loading = false;
        }
        Encoding = loaded.Encoding;
        Eol = loaded.Eol;
    }

    public Task<string> GetTextAsync() => Task.FromResult(_box.Text);

    public void SetFileName(string? fileName)
    {
    }

    public void Find()
    {
    }

    public void Replace()
    {
    }

    public void GoToLine()
    {
    }

    public Task<string> FormatAsync() => Task.FromResult("unsupported");

    public void TogglePreview()
    {
    }

    public void Reveal(int line, int column = 1, int length = 0)
    {
    }

    public void ApplySettings(AppSettings settings)
    {
        _box.TextWrapping = settings.WordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        ScrollViewer.SetHorizontalScrollBarVisibility(_box, settings.WordWrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
        try
        {
            _box.FontFamily = new FontFamily(settings.FontFamily + ", Cascadia Mono, Consolas");
        }
        catch (Exception)
        {
            _box.FontFamily = new FontFamily("Consolas");
        }
        _baseFontSize = Math.Clamp(settings.FontSize, 8, 48) * Density.EditorScale;
        ApplyZoom();
    }

    public void ZoomBy(int percent) => SetZoom(_zoom + percent);

    public void SetZoom(int percent)
    {
        int clamped = Math.Clamp(percent, MinZoom, MaxZoom);
        if (clamped == _zoom) return;
        _zoom = clamped;
        ApplyZoom();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyZoom() => _box.FontSize = Math.Max(6, _baseFontSize * _zoom / 100.0);

    public void FocusEditor() => _box.Focus(FocusState.Programmatic);

    public void Undo() => _box.Undo();
    public void Redo() => _box.Redo();
    public void Cut() => _box.CutSelectionToClipboard();
    public void Copy() => _box.CopySelectionToClipboard();
    public void Paste() => _box.PasteFromClipboard();
    public void SelectAll() => _box.SelectAll();

    /// <summary>İmlecin satır ve sütunu (1 tabanlı), metin uzunluğu ve seçili karakter sayısı.</summary>
    public CaretInfo GetCaret()
    {
        string text = _box.Text;
        int position = Math.Clamp(_box.SelectionStart, 0, text.Length);
        int line = 1, lastBreak = -1;
        for (int i = 0; i < position; i++)
        {
            char c = text[i];
            if (c == '\r' || c == '\n')
            {
                // "\r\n" tek satır sonu sayılır
                if (c == '\n' && i > 0 && text[i - 1] == '\r')
                {
                    lastBreak = i;
                    continue;
                }
                line++;
                lastBreak = i;
            }
        }
        return new CaretInfo(line, position - lastBreak, text.Length, _box.SelectionLength);
    }

    private void Box_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Tab tuşu odağı değiştirmesin, sekme karakteri yazsın.
        if (e.Key != VirtualKey.Tab || IsDown(VirtualKey.Control) || IsDown(VirtualKey.Menu)) return;
        e.Handled = true;
        if (IsDown(VirtualKey.Shift)) return;
        int start = _box.SelectionStart;
        _box.SelectedText = "\t";
        _box.SelectionStart = start + 1;
        _box.SelectionLength = 0;
    }

    private void Box_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!IsDown(VirtualKey.Control)) return;
        int delta = e.GetCurrentPoint(_box).Properties.MouseWheelDelta;
        if (delta == 0) return;
        ZoomBy(delta > 0 ? 10 : -10);
        e.Handled = true;
    }

    private static string NormalizeBreaks(string text) => text.Replace("\r\n", "\r").Replace('\n', '\r');

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
