using Windows.Storage;

namespace ZeroDataPeek.Services;

/// <summary>
/// Dosya Gezgini sağ tık menüsündeki "Zero Data Peek ile aç" komutunun hangi dosya türlerinde görüneceği.
/// Seçim Ayarlar'dan yapılır ve uygulamanın LocalState klasöründeki shell-types.txt dosyasına yazılır;
/// menüyü üreten ZdpShell.dll (src/ZdpShell) her açılışta bu dosyayı okur.
/// </summary>
public static class ShellMenu
{
    private const string FileName = "shell-types.txt";

    /// <summary>Ayarlar'da gösterilen gruplar ve türler.</summary>
    public static readonly (string Group, string[] Extensions)[] Groups =
    {
        ("Veri", new[] { ".csv", ".tsv", ".parquet", ".json", ".jsonl", ".xlsx", ".xls" }),
        ("Belge", new[] { ".docx" }),
        ("Kod ve metin", new[] { ".txt", ".log", ".md", ".sql", ".py", ".xml", ".yaml", ".yml", ".ini", ".ps1", ".js", ".cs" }),
        ("Oturum", new[] { ".zdp" }),
    };

    /// <summary>Kullanıcı hiç seçim yapmadıysa etkin türler; ZdpShell.cpp içindeki kDefaultExtensions ile aynı tutulmalı.</summary>
    public static readonly string[] Defaults = { ".csv", ".tsv", ".parquet", ".json", ".jsonl", ".xlsx", ".zdp" };

    /// <summary>Etkin türler: kayıtlı seçim, yoksa varsayılanlar.</summary>
    public static IReadOnlyList<string> Enabled(AppSettings settings)
    {
        string saved = settings.ShellTypes;
        if (saved == "-") return Array.Empty<string>(); // kullanıcı hepsini kapattı
        return saved.Length == 0 ? Defaults : saved.Split('|', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>Seçimi ayarlara ve sağ tık eklentisinin okuduğu dosyaya yazar.</summary>
    public static void Save(AppSettings settings, IEnumerable<string> extensions)
    {
        var list = extensions.Select(e => e.Trim().ToLowerInvariant()).Where(e => e.Length > 1 && e[0] == '.').Distinct().ToList();
        settings.ShellTypes = list.Count == 0 ? "-" : string.Join('|', list);
        Publish(settings);
    }

    /// <summary>Eklentinin okuduğu dosyayı ayarlarla eşitler (her açılışta ve seçim değişince).</summary>
    public static void Publish(AppSettings settings)
    {
        try
        {
            string path = Path.Combine(ApplicationData.Current.LocalFolder.Path, FileName);
            File.WriteAllText(path, string.Join("\r\n", Enabled(settings)) + "\r\n", System.Text.Encoding.ASCII);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Sağ tık menüsü ayarı", ex);
        }
    }
}
