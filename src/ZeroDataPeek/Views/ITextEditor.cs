using ZeroDataPeek.Services;

namespace ZeroDataPeek.Views;

/// <summary>İmleç konumu ve metin uzunluğu (durum çubuğu için).</summary>
public readonly record struct CaretInfo(int Line, int Column, int Length, int Selected);

/// <summary>
/// Metin sekmelerinin ortak yüzü. İki uygulaması var: Monaco tabanlı kod düzenleyici (<see cref="CodeDocumentView"/>)
/// ve o kullanılamadığında devreye giren düz metin kutusu (<see cref="TextDocumentView"/>).
/// </summary>
public interface ITextEditor
{
    /// <summary>Kullanıcı metni değiştirdi.</summary>
    event EventHandler? Modified;

    /// <summary>İmleç ya da seçim değişti.</summary>
    event EventHandler? CaretMoved;

    /// <summary>Yakınlaştırma değişti.</summary>
    event EventHandler? ZoomChanged;

    /// <summary>Kaydederken kullanılacak kodlama.</summary>
    EncodingChoice Encoding { get; set; }

    /// <summary>Kaydederken kullanılacak satır sonu: "\r\n", "\n" ya da "\r".</summary>
    string Eol { get; set; }

    int Zoom { get; }

    /// <summary>Dosyadan okunan içeriği yükler; geri alma geçmişini temizler.</summary>
    void Load(LoadedText loaded);

    /// <summary>Dosya adı (sözdizimi renklendirmede dili seçmek için).</summary>
    void SetFileName(string? fileName);

    /// <summary>Düzenleyicideki metin (satır sonları kaydederken dönüştürülür).</summary>
    Task<string> GetTextAsync();

    void ApplySettings(AppSettings settings);
    void ZoomBy(int percent);
    void SetZoom(int percent);
    void FocusEditor();

    void Undo();
    void Redo();
    void Cut();
    void Copy();
    void Paste();
    void SelectAll();
    void Find();
    void Replace();
    void GoToLine();

    /// <summary>Belgeyi dile göre biçimlendirir (SQL, JSON, XML…). Dönen değer: "ok", "unsupported" ya da "error: …".</summary>
    Task<string> FormatAsync();

    /// <summary>Markdown önizlemeyi açar ya da kapatır.</summary>
    void TogglePreview();

    /// <summary>İmleci verilen satıra götürür ve satırı görünür yapar.</summary>
    void Reveal(int line);

    CaretInfo GetCaret();
}
