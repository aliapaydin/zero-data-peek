namespace ZeroDataPeek.Services;

/// <summary>
/// Arayüz yoğunluğu. Kompakt modda satırlar alçalır, yazılar küçülür, boşluklar daralır; aynı ekrana daha çok şey sığar.
/// Liste şablonları değerleri buradan okur; mod değişince listeler yeniden kurulur (bkz. MainWindow.ApplyDensity).
/// </summary>
public static class Density
{
    public static bool Compact { get; set; }

    public static double ExplorerRow => Compact ? 21 : 28;
    public static double TabRow => Compact ? 24 : 34;
    public static double ListFont => Compact ? 12 : 14;
    public static double ListIcon => Compact ? 12 : 14;
    public static double ListItemMinHeight => Compact ? 22 : 32;
    public static double TitleBarHeight => Compact ? 34 : 40;
    public static double MenuHeight => Compact ? 30 : 40;
    public static double ContentPadding => Compact ? 3 : 8;
    public static double CardRadius => Compact ? 6 : 10;

    public static double GridRow => Compact ? 21 : 28;
    public static double GridHeader => Compact ? 25 : 34;
    public static double GridFont => Compact ? 12 : 13;
    public static double GridCellPadding => Compact ? 6 : 10;

    /// <summary>Kod düzenleyici yazı boyutu çarpanı.</summary>
    public static double EditorScale => Compact ? 0.9 : 1.0;
}
