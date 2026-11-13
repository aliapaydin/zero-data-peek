using System.ComponentModel;
using Microsoft.UI.Xaml;
using ZeroDataPeek.Services;
using ZeroDataPeek.Views;

namespace ZeroDataPeek.Models;

/// <summary>Açık bir sekme: dosya yolu, başlık, kaydedilmemiş değişiklik durumu ve içerik görünümü.</summary>
public sealed partial class DocumentTab : INotifyPropertyChanged
{
    private string? _filePath;
    private string _title;
    private bool _isDirty;

    public DocumentTab(string title, string? filePath, FrameworkElement view)
    {
        _title = title;
        _filePath = filePath;
        View = view;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>İçerik görünümü (düzenleyici ya da bilgi sayfası).</summary>
    public FrameworkElement View { get; }

    /// <summary>Görünüm bir metin düzenleyiciyse o; değilse null.</summary>
    public ITextEditor? Editor => View as ITextEditor;

    /// <summary>Görünüm zengin metin belgesiyse o; değilse null.</summary>
    public RichDocumentView? Doc => View as RichDocumentView;

    /// <summary>Görünüm bir veri tablosuysa o; değilse null.</summary>
    public DataDocumentView? Data => View as DataDocumentView;

    /// <summary>Diskteki tam yol; henüz kaydedilmemiş yeni sekmede null.</summary>
    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (_filePath == value) return;
            _filePath = value;
            if (value is not null) _title = Path.GetFileName(value);
            RaiseAll();
        }
    }

    public string Title => _title;

    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (_isDirty == value) return;
            _isDirty = value;
            RaiseAll();
        }
    }

    public DocumentKind Kind => View is RichDocumentView ? DocumentKind.Document : FileKinds.KindOf(_filePath);

    public string Glyph => FileKinds.Glyph(Kind);

    /// <summary>Sekme başlığı; kaydedilmemiş değişiklik varsa başında nokta.</summary>
    public string DisplayTitle => _isDirty ? "● " + _title : _title;

    public string ToolTip => _filePath ?? _title;

    public string CloseTip => Loc.T("Sekmeyi kapat");

    /// <summary>Dikey sekme listesindeki satır yüksekliği ve yazı boyutu (kompakt moda göre).</summary>
    public double RowHeight => Density.TabRow;
    public double FontSize => Density.ListFont;
    public double IconSize => Density.ListIcon;

    /// <summary>Dosyanın en son okunduğu ya da yazıldığı andaki değişiklik zamanı (diskte değişti mi denetimi için).</summary>
    public DateTime? DiskStamp { get; set; }

    /// <summary>Dosyanın şu anki değişiklik zamanını kaydeder (açtıktan ve kaydettikten sonra).</summary>
    public void TouchDiskStamp()
    {
        try
        {
            DiskStamp = _filePath is not null && File.Exists(_filePath) ? File.GetLastWriteTimeUtc(_filePath) : null;
        }
        catch (Exception)
        {
            DiskStamp = null;
        }
    }

    private void RaiseAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
