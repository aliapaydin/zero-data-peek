using Microsoft.UI.Xaml;
using ZeroDataPeek.Services;

namespace ZeroDataPeek.Models;

/// <summary>Dosyalarda arama sonucundaki bir satır: dosya başlığı ya da eşleşen satır.</summary>
public sealed class SearchHit
{
    private SearchHit(string path)
    {
        Path = path;
    }

    public string Path { get; }
    public bool IsFile { get; private init; }

    /// <summary>Eşleşmenin satırı, sütunu (1'den başlar) ve uzunluğu.</summary>
    public int Line { get; private init; }
    public int Column { get; private init; }
    public int Length { get; private init; }

    /// <summary>Satır metni üç parça halinde: eşleşmeden önce, eşleşme, sonra. Dosya başlığında yalnızca Before dolu (göreli yol).</summary>
    public string Before { get; private init; } = string.Empty;
    public string Match { get; private init; } = string.Empty;
    public string After { get; private init; } = string.Empty;

    /// <summary>Sol sütun: satır numarası ya da dosya simgesi.</summary>
    public string LineText => IsFile ? string.Empty : Line.ToString(System.Globalization.CultureInfo.CurrentCulture);
    public string Glyph => IsFile ? FileKinds.Glyph(FileKinds.KindOf(Path)) : string.Empty;

    /// <summary>Sağdaki sayı rozeti (dosyadaki eşleşme sayısı).</summary>
    public string CountText { get; private init; } = string.Empty;

    public Windows.UI.Text.FontWeight Weight => IsFile ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
    /// <summary>Dosya adları arayüz yazı tipiyle, kod satırları eş aralıklı yazı tipiyle. (Arayüz iş parçacığında, ilk bağlamada oluşturulur.)</summary>
    public Microsoft.UI.Xaml.Media.FontFamily Font => IsFile
        ? _uiFont ??= new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI Variable Text, Segoe UI")
        : _monoFont ??= new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas, Courier New");

    private static Microsoft.UI.Xaml.Media.FontFamily? _uiFont, _monoFont;
    public double RowHeight => Density.Compact ? (IsFile ? 24 : 20) : (IsFile ? 32 : 26);
    public double FontSize => Density.Compact ? 12 : 13;
    public Thickness RowMargin => IsFile ? new Thickness(0, 4, 0, 0) : new Thickness(0);
    public string ToolTip => IsFile ? Path : Before + Match + After;

    public static SearchHit ForFile(string path, string relative, int count) => new(path)
    {
        IsFile = true,
        Before = relative,
        CountText = count.ToString(System.Globalization.CultureInfo.CurrentCulture),
    };

    public static SearchHit ForMatch(string path, int line, int column, int length, string before, string match, string after) => new(path)
    {
        Line = line,
        Column = column,
        Length = length,
        Before = before,
        Match = match,
        After = after,
    };
}
