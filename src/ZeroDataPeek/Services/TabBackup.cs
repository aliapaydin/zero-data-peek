using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Storage;

namespace ZeroDataPeek.Services;

/// <summary>Kaydedilmemiş bir metin sekmesinin yedeği.</summary>
public sealed class BackupEntry
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Dosyanın yolu; hiç kaydedilmemiş sekmede boş.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("encoding")]
    public string Encoding { get; set; } = "utf-8";

    [JsonPropertyName("eol")]
    public string Eol { get; set; } = "\r\n";

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Kaydedilmemiş sekmelerin saklandığı yer (LocalState\unsaved). Uygulama kapanırken ve çalışırken belirli aralıklarla
/// yazılır; bir sonraki açılışta sekmeler değişiklikleriyle geri gelir. Yalnızca metin sekmeleri saklanır.
/// </summary>
public static class TabBackup
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string Folder
    {
        get
        {
            string root;
            try
            {
                root = ApplicationData.Current.LocalFolder.Path;
            }
            catch (Exception)
            {
                root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ZeroDataPeek");
            }
            string folder = System.IO.Path.Combine(root, "unsaved");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    /// <summary>Verilen sekmeleri yazar ve eski yedekleri siler. Hata fırlatabilir (çağıran karar verir).</summary>
    public static void Save(IReadOnlyList<BackupEntry> entries)
    {
        string folder = Folder;
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < entries.Count; i++)
        {
            string target = System.IO.Path.Combine(folder, "tab-" + i.ToString("000", System.Globalization.CultureInfo.InvariantCulture) + ".json");
            string temp = target + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(entries[i], Options), new System.Text.UTF8Encoding(false));
            File.Move(temp, target, overwrite: true);
            keep.Add(target);
        }
        foreach (var file in Directory.GetFiles(folder))
        {
            if (!keep.Contains(file)) TryDelete(file);
        }
    }

    public static List<BackupEntry> Load()
    {
        var result = new List<BackupEntry>();
        try
        {
            foreach (var file in Directory.GetFiles(Folder, "tab-*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var entry = JsonSerializer.Deserialize<BackupEntry>(File.ReadAllText(file), Options);
                    if (entry is not null) result.Add(entry);
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                {
                    CrashLog.Write("Yedek okuma", ex);
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Yedek klasörü", ex);
        }
        return result;
    }

    public static void Clear()
    {
        try
        {
            foreach (var file in Directory.GetFiles(Folder)) TryDelete(file);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Yedek temizleme", ex);
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception)
        {
            // bir sonraki yazımda yeniden denenir
        }
    }
}
