using System.Runtime.CompilerServices;
using Windows.Storage;

namespace ZeroDataPeek.Services;

public enum AppTheme
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>Kalıcı kullanıcı ayarları (paketli uygulamanın LocalSettings deposu).</summary>
public sealed class AppSettings
{
    public const double DefaultFontSize = 14;
    public const string DefaultFontFamily = "Cascadia Mono";

    public static AppSettings Current { get; } = new();

    private readonly ApplicationDataContainer? _store;
    private readonly Dictionary<string, object> _memory = new();

    private AppSettings()
    {
        try
        {
            _store = ApplicationData.Current.LocalSettings;
        }
        catch (Exception)
        {
            _store = null; // paketsiz çalıştırma: ayarlar yalnızca bellekte
        }
    }

    // ---- Görünüm ----
    public AppTheme Theme { get => (AppTheme)Get(0); set => Set((int)value); }
    /// <summary>Sekmeler solda dikey listede mi (false: üstte yatay şerit).</summary>
    public bool VerticalTabs { get => Get(false); set => Set(value); }
    public double TabPaneWidth { get => Get(240.0); set => Set(value); }
    public bool ShowStatusBar { get => Get(true); set => Set(value); }
    /// <summary>Arayüz dili: "" (Windows'a göre), "tr" ya da "en". Yeniden başlatınca uygulanır.</summary>
    public string UiLanguage { get => Get(string.Empty); set => Set(value); }

    // ---- Düzenleyici ----
    public bool WordWrap { get => Get(false); set => Set(value); }
    public string FontFamily { get => Get(DefaultFontFamily); set => Set(value); }
    public double FontSize { get => Get(DefaultFontSize); set => Set(value); }

    // ---- Oturum ----
    /// <summary>Açılışta, son kapanışta açık olan dosyaları geri getir.</summary>
    public bool RestoreSession { get => Get(true); set => Set(value); }
    public IReadOnlyList<string> SessionFiles
    {
        get => Split(Get(string.Empty));
        set => Set(Join(value, 40));
    }
    public string SessionActiveFile { get => Get(string.Empty); set => Set(value); }

    // ---- Yerleşim (hatırlanır) ----
    public string MainWindowBounds { get => Get(string.Empty); set => Set(value); }

    // ---- Son açılanlar ----
    public IReadOnlyList<string> RecentFiles
    {
        get => Split(Get(string.Empty));
        set => Set(Join(value, 12));
    }

    public void AddRecent(string path)
    {
        var list = RecentFiles.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToList();
        list.Insert(0, path);
        RecentFiles = list;
    }

    public void RemoveRecent(string path) =>
        RecentFiles = RecentFiles.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToList();

    private static IReadOnlyList<string> Split(string joined) =>
        joined.Split('|', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>LocalSettings tek değerde yaklaşık 8 KB tutar; listeyi hem adet hem uzunlukla sınırla.</summary>
    private static string Join(IEnumerable<string> paths, int max)
    {
        var kept = new List<string>();
        int length = 0;
        foreach (var path in paths.Take(max))
        {
            if (length + path.Length + 1 > 3500) break;
            kept.Add(path);
            length += path.Length + 1;
        }
        return string.Join('|', kept);
    }

    private T Get<T>(T fallback, [CallerMemberName] string key = "")
    {
        try
        {
            if (_store is not null)
                return _store.Values.TryGetValue(key, out var value) && value is T typed ? typed : fallback;
            return _memory.TryGetValue(key, out var stored) && stored is T mem ? mem : fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private void Set<T>(T value, [CallerMemberName] string key = "") where T : notnull
    {
        try
        {
            if (_store is not null) _store.Values[key] = value;
            else _memory[key] = value;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Ayar", ex);
        }
    }
}

public static class ThemeHelper
{
    public static Microsoft.UI.Xaml.ElementTheme ToElementTheme(AppTheme theme) => theme switch
    {
        AppTheme.Light => Microsoft.UI.Xaml.ElementTheme.Light,
        AppTheme.Dark => Microsoft.UI.Xaml.ElementTheme.Dark,
        _ => Microsoft.UI.Xaml.ElementTheme.Default,
    };
}
