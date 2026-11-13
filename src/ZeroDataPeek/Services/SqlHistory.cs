using System.Text.Json;
using Windows.Storage;

namespace ZeroDataPeek.Services;

/// <summary>Çalıştırılan son SQL sorguları (LocalState\sql-history.json); SQL panelindeki "Geçmiş" düğmesi buradan beslenir.</summary>
public static class SqlHistory
{
    private const int Max = 40;
    private static List<string>? _items;

    private static string FilePath
    {
        get
        {
            try
            {
                return Path.Combine(ApplicationData.Current.LocalFolder.Path, "sql-history.json");
            }
            catch (Exception)
            {
                return Path.Combine(Path.GetTempPath(), "zdp-sql-history.json");
            }
        }
    }

    /// <summary>En yeni sorgu başta.</summary>
    public static IReadOnlyList<string> Items
    {
        get
        {
            if (_items is not null) return _items;
            try
            {
                _items = File.Exists(FilePath) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? new List<string>() : new List<string>();
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _items = new List<string>();
            }
            return _items;
        }
    }

    public static void Add(string sql)
    {
        string text = sql.Trim();
        if (text.Length == 0 || text.Length > 20000) return;
        var list = Items.Where(q => !string.Equals(q, text, StringComparison.Ordinal)).ToList();
        list.Insert(0, text);
        if (list.Count > Max) list.RemoveRange(Max, list.Count - Max);
        _items = list;
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write("SQL geçmişi", ex);
        }
    }
}
