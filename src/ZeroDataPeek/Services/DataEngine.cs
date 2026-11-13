using System.Data.Common;
using System.Globalization;
using System.Text;
using DuckDB.NET.Data;
using ExcelDataReader;

namespace ZeroDataPeek.Services;

/// <summary>Bir veri kolonunun adı ve DuckDB türü.</summary>
public sealed record DataColumn(string Name, string Type)
{
    private static readonly string[] NumericPrefixes =
    {
        "TINYINT", "SMALLINT", "INTEGER", "BIGINT", "HUGEINT", "UTINYINT", "USMALLINT", "UINTEGER", "UBIGINT", "UHUGEINT",
        "FLOAT", "DOUBLE", "DECIMAL",
    };

    /// <summary>Sayısal tür mü (liste ve dizi türleri, ör. INTEGER[], sayısal sayılmaz).</summary>
    public bool IsNumeric => !Type.Contains('[') && NumericPrefixes.Any(p => Type.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Veritabanındaki bir tablo ya da görünüm: dosyanın kendisi, bir sorgu sonucu ya da sıralanmış/süzülmüş hali.</summary>
public sealed record DataRelation(string Name, bool IsTable, IReadOnlyList<DataColumn> Columns, long RowCount);

/// <summary>
/// Bir dosyanın açılış sonucu; <see cref="Note"/> okuma sırasında yapılan uzlaşmayı (varsa) anlatır.
/// Excel dosyalarında <see cref="Sheets"/> sayfa adlarını, <see cref="Sheet"/> açılan sayfanın sırasını verir.
/// </summary>
public sealed record OpenedData(DataRelation Relation, string? Note, IReadOnlyList<string> Sheets, int Sheet);

/// <summary>
/// CSV okuma seçenekleri (kullanıcı otomatik algılamayı beğenmediğinde). Boş değer "otomatik" demektir.
/// Delimiter: "," ";" "\t" "|" · Decimal: "." ya da "," · Header: 0 otomatik, 1 var, 2 yok · Encoding: "utf-8", "utf-16", "ansi".
/// </summary>
public sealed record CsvOptions(string Delimiter = "", string Decimal = "", int Header = 0, string Encoding = "", bool AllText = false)
{
    public bool IsDefault => Delimiter.Length == 0 && Decimal.Length == 0 && Header == 0 && Encoding.Length == 0 && !AllText;
}

/// <summary>Tek kolona uygulanan değer süzgeci: değere eşit olanlar ya da (Exclude) olmayanlar. Value null ise boş (NULL) değerler.</summary>
public sealed record DataFilter(string Column, string? Value, bool Exclude, FilterOp Op = FilterOp.Equals, string? Value2 = null);

/// <summary>Süzgeç koşulu. Equals, Contains, StartsWith, EndsWith ve Empty, <see cref="DataFilter.Exclude"/> ile tersine çevrilebilir.</summary>
public enum FilterOp
{
    Equals,
    Contains,
    StartsWith,
    EndsWith,
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,
    Between,
    Empty,
}

/// <summary>Tek kolon için özet istatistikler.</summary>
public sealed record ColumnProfile(
    DataColumn Column, long Rows, long NonNull, long Distinct, string? Min, string? Max, string? Average, string? Median,
    IReadOnlyList<(string? Value, long Count)> TopValues);

public enum ExportFormat
{
    Csv,
    Tsv,
    Parquet,
    Json,
    JsonLines,
}

/// <summary>Veri motorundan kullanıcıya gösterilecek hata.</summary>
public sealed class DataEngineException : Exception
{
    public DataEngineException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>
/// Veri modu motoru: gömülü DuckDB. Tüm veri sekmeleri tek veritabanını paylaşır; böylece bir sekmedeki SQL
/// diğer açık dosyalara adlarıyla erişebilir. Veritabanı geçici klasörde durur, büyük dosyalar belleği doldurmaz.
/// Bağlantı tek olduğu için çağrılar sıraya girer ve arka planda çalışır.
/// </summary>
public sealed class DataEngine : IDisposable
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "all", "and", "as", "by", "case", "column", "create", "default", "desc", "distinct", "end", "from", "group", "having",
        "in", "join", "limit", "not", "null", "on", "or", "order", "select", "table", "to", "union", "user", "using", "values",
        "when", "where", "with", "data", "main", "temp",
    };

    public static DataEngine Shared { get; } = new();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _tempFiles = new();
    private DuckDBConnection? _connection;
    private string? _databasePath;
    private int _counter;

    private DataEngine()
    {
    }

    /// <summary>Hangi uzantılar tablo olarak açılabiliyor.</summary>
    public static bool Supports(string? path)
    {
        string ext = SafeExtension(path);
        return ext is ".csv" or ".tsv" or ".tab" or ".parquet" or ".json" or ".jsonl" or ".ndjson" || IsExcel(path);
    }

    public static bool IsExcel(string? path) => SafeExtension(path) is ".xlsx" or ".xlsm" or ".xlsb" or ".xls";

    /// <summary>Açık dosyaların SQL'de kullanılabilen tablo adları.</summary>
    public IReadOnlyList<string> TableNames
    {
        get
        {
            lock (_names) return _names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    /// <summary>Sorgu ve görünüm tabloları için benzersiz iç ad.</summary>
    public string NewInternalName(string prefix) =>
        "__zdp_" + prefix + Interlocked.Increment(ref _counter).ToString(CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ dosya açma

    /// <summary>
    /// Dosyayı veritabanına alır. <paramref name="existingName"/> verilirse (Excel'de sayfa değiştirirken) aynı tablo adı yeniden kullanılır.
    /// </summary>
    public Task<OpenedData> OpenFileAsync(string path, int sheet = 0, string? existingName = null, CsvOptions? csv = null) => RunAsync(connection =>
    {
        string name = existingName ?? ReserveName(path);
        try
        {
            string ext = SafeExtension(path);
            string? note = null;
            bool isTable = true;
            IReadOnlyList<string> sheets = Array.Empty<string>();

            if (IsExcel(path))
            {
                string csv = ExcelSheetToCsv(path, ref sheet, out sheets);
                note = ImportCsv(connection, name, csv, sniff: false);
            }
            else if (ext == ".parquet")
            {
                // Parquet kolon bazlı ve dizinli: içeri kopyalamadan doğrudan okunur, dev dosyalar anında açılır.
                Execute(connection, $"CREATE OR REPLACE VIEW {Quote(name)} AS SELECT * FROM read_parquet({Literal(path)})");
                isTable = false;
            }
            else if (ext is ".json" or ".jsonl" or ".ndjson")
            {
                Execute(connection, $"CREATE OR REPLACE TABLE {Quote(name)} AS SELECT * FROM read_json_auto({Literal(path)})");
            }
            else
            {
                note = csv is null || csv.IsDefault
                    ? ImportCsv(connection, name, path, sniff: true)
                    : ImportCsvWithOptions(connection, name, path, csv);
            }

            return new OpenedData(Describe(connection, name, isTable), note, sheets, sheet);
        }
        catch
        {
            if (existingName is null) ReleaseName(name);
            throw;
        }
    });

    /// <summary>
    /// Excel sayfasını geçici bir UTF-8 CSV'ye yazar (DuckDB'nin Excel eklentisi internetten indirildiği için kullanılmaz).
    /// İlk dolu satır başlık olur; tarih ve sayılar kültürden bağımsız yazılır.
    /// </summary>
    private string ExcelSheetToCsv(string path, ref int sheet, out IReadOnlyList<string> sheets)
    {
        var names = new List<string>();
        string target = Path.Combine(TempFolder, "xl-" + Guid.NewGuid().ToString("N") + ".csv");
        lock (_tempFiles) _tempFiles.Add(target);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        if (sheet < 0 || sheet >= reader.ResultsCount) sheet = 0;

        int index = 0;
        bool written = false;
        do
        {
            names.Add(reader.Name ?? ("Sayfa" + (index + 1).ToString(CultureInfo.InvariantCulture)));
            if (index == sheet)
            {
                using var writer = new StreamWriter(target, false, new UTF8Encoding(false));
                var line = new StringBuilder();
                while (reader.Read())
                {
                    line.Clear();
                    bool any = false;
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        if (i > 0) line.Append(',');
                        string value = CsvValue(reader.GetValue(i));
                        if (value.Length > 0) any = true;
                        line.Append(value);
                    }
                    if (!any) continue; // tümüyle boş satır
                    writer.Write(line);
                    writer.Write('\n');
                    written = true;
                }
            }
            index++;
        }
        while (reader.NextResult());

        sheets = names;
        if (!written) throw new DataEngineException(Loc.T("Bu sayfada veri yok."));
        return target;
    }

    private static string CsvValue(object? value) => value switch
    {
        null or DBNull => string.Empty,
        DateTime date => date.TimeOfDay == TimeSpan.Zero
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        TimeSpan time => time.ToString("c", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        float number => number.ToString("R", CultureInfo.InvariantCulture),
        decimal number => number.ToString(CultureInfo.InvariantCulture),
        sbyte or byte or short or ushort or int or uint or long or ulong => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => "\"" + (Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty).Replace("\"", "\"\"") + "\"",
    };

    /// <summary>
    /// CSV'yi tabloya alır. Önce türleri algılayarak dener; olmazsa hatalı satırları atlayıp tüm kolonları metin olarak alır.
    /// UTF-8 olmayan dosyalar (ör. Windows-1254) önce geçici bir UTF-8 kopyaya çevrilir.
    /// </summary>
    private string? ImportCsv(DbConnection connection, string name, string path, bool sniff)
    {
        string source = path;
        string? note = null;
        string encodingOption = string.Empty;

        var encoding = sniff ? SniffEncoding(path) : SniffedEncoding.Utf8;
        if (encoding == SniffedEncoding.Utf16)
        {
            encodingOption = ", encoding='utf-16'";
        }
        else if (encoding == SniffedEncoding.Ansi)
        {
            source = TranscodeToUtf8(path, out string encodingName);
            note = Loc.F("Dosya {0} kodlamasından UTF-8'e çevrilerek okundu.", encodingName);
        }

        // Ayraç noktalı virgülse (Türkçe ve Avrupa Excel dışa aktarımları) ondalık ayırıcı virgüldür.
        if (sniff && SniffDelimiter(connection, source, encodingOption) == ";") encodingOption += ", decimal_separator=','";

        string create = $"CREATE OR REPLACE TABLE {Quote(name)} AS SELECT * FROM read_csv({Literal(source)}";
        try
        {
            Execute(connection, create + encodingOption + ")");
            return note;
        }
        catch (Exception first)
        {
            try
            {
                Execute(connection, create + encodingOption + ", all_varchar=true, ignore_errors=true, null_padding=true)");
            }
            catch (Exception)
            {
                if (!sniff || source != path || encoding == SniffedEncoding.Utf16) throw first;
                // Örnekte görünmeyen geçersiz baytlar olabilir: ANSI sayıp çevirerek son bir kez dene.
                source = TranscodeToUtf8(path, out string encodingName);
                Execute(connection, $"CREATE OR REPLACE TABLE {Quote(name)} AS SELECT * FROM read_csv({Literal(source)}, all_varchar=true, ignore_errors=true, null_padding=true)");
                return Loc.F("Dosya {0} kodlamasından UTF-8'e çevrilerek okundu.", encodingName) + " " +
                       Loc.T("Dosya düzensiz olduğu için tüm kolonlar metin olarak alındı; okunamayan satırlar atlanmış olabilir.");
            }
            string lenient = Loc.T("Dosya düzensiz olduğu için tüm kolonlar metin olarak alındı; okunamayan satırlar atlanmış olabilir.");
            return note is null ? lenient : note + " " + lenient;
        }
    }

    /// <summary>CSV'yi kullanıcının verdiği seçeneklerle okur; verilmeyenler DuckDB'nin algılamasına kalır.</summary>
    private string? ImportCsvWithOptions(DbConnection connection, string name, string path, CsvOptions csv)
    {
        string source = path;
        string? note = null;
        var options = new StringBuilder();

        string encoding = csv.Encoding;
        if (encoding.Length == 0)
        {
            var sniffed = SniffEncoding(path);
            encoding = sniffed == SniffedEncoding.Utf16 ? "utf-16" : sniffed == SniffedEncoding.Ansi ? "ansi" : "utf-8";
        }
        if (encoding == "utf-16")
        {
            options.Append(", encoding='utf-16'");
        }
        else if (encoding == "ansi")
        {
            source = TranscodeToUtf8(path, out string encodingName);
            note = Loc.F("Dosya {0} kodlamasından UTF-8'e çevrilerek okundu.", encodingName);
        }

        if (csv.Delimiter.Length > 0) options.Append(", delim=").Append(Literal(csv.Delimiter));
        if (csv.Decimal == ",") options.Append(", decimal_separator=','");
        if (csv.Header == 1) options.Append(", header=true");
        else if (csv.Header == 2) options.Append(", header=false");
        if (csv.AllText) options.Append(", all_varchar=true");

        Execute(connection, $"CREATE OR REPLACE TABLE {Quote(name)} AS SELECT * FROM read_csv({Literal(source)}{options})");
        return note;
    }

    private static string? SniffDelimiter(DbConnection connection, string path, string options)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT Delimiter FROM sniff_csv({Literal(path)}{options})";
            return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return null; // algılanamadı: varsayılanlarla okunur
        }
    }

    private enum SniffedEncoding
    {
        Utf8,
        Utf16,
        Ansi,
    }

    private static SniffedEncoding SniffEncoding(string path)
    {
        byte[] buffer = new byte[4 * 1024 * 1024];
        int read;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            read = stream.Read(buffer, 0, buffer.Length);
        }
        if (read >= 2 && ((buffer[0] == 0xFF && buffer[1] == 0xFE) || (buffer[0] == 0xFE && buffer[1] == 0xFF))) return SniffedEncoding.Utf16;
        try
        {
            // flush: false → örneğin sonunda yarım kalan çok baytlı karakter hata sayılmaz.
            new UTF8Encoding(false, true).GetDecoder().GetCharCount(buffer, 0, read, flush: false);
            return SniffedEncoding.Utf8;
        }
        catch (DecoderFallbackException)
        {
            return SniffedEncoding.Ansi;
        }
    }

    private string TranscodeToUtf8(string path, out string encodingName)
    {
        int codePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
        Encoding encoding;
        try
        {
            encoding = Encoding.GetEncoding(codePage > 0 ? codePage : 1254);
        }
        catch (Exception)
        {
            encoding = Encoding.GetEncoding(1254);
        }
        encodingName = encoding.WebName;

        string target = Path.Combine(TempFolder, "csv-" + Guid.NewGuid().ToString("N") + ".csv");
        using (var reader = new StreamReader(path, encoding, detectEncodingFromByteOrderMarks: false))
        using (var writer = new StreamWriter(target, false, new UTF8Encoding(false)))
        {
            char[] chunk = new char[64 * 1024];
            int count;
            while ((count = reader.Read(chunk, 0, chunk.Length)) > 0) writer.Write(chunk, 0, count);
        }
        lock (_tempFiles) _tempFiles.Add(target);
        return target;
    }

    // ------------------------------------------------------------------ okuma

    /// <summary>Satır aralığını, tüm değerler metne çevrilmiş olarak getirir (null değerler null kalır).</summary>
    public Task<string?[][]> FetchAsync(DataRelation relation, long offset, int count) => RunAsync(connection =>
    {
        if (relation.Columns.Count == 0 || count <= 0) return Array.Empty<string?[]>();
        string columns = string.Join(", ", relation.Columns.Select(c => $"CAST({Quote(c.Name)} AS VARCHAR)"));
        string o = offset.ToString(CultureInfo.InvariantCulture);
        string end = (offset + count).ToString(CultureInfo.InvariantCulture);
        string sql = relation.IsTable
            ? $"SELECT {columns} FROM {Quote(relation.Name)} WHERE rowid >= {o} AND rowid < {end} ORDER BY rowid"
            : $"SELECT {columns} FROM {Quote(relation.Name)} LIMIT {count.ToString(CultureInfo.InvariantCulture)} OFFSET {o}";

        var rows = new List<string?[]>(count);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        int width = relation.Columns.Count;
        while (reader.Read())
        {
            var row = new string?[width];
            for (int i = 0; i < width; i++)
            {
                row[i] = reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
            }
            rows.Add(row);
        }
        return rows.ToArray();
    });

    /// <summary>Kaynağı arama metni ve sıralamayla yeni bir tabloya alır (sayfalama hızlı kalsın diye sonuç saklanır).</summary>
    public Task<DataRelation> DeriveAsync(DataRelation source, string target, string? search, IReadOnlyList<DataFilter> filters,
        string? sortColumn, bool descending) => RunAsync(connection =>
    {
        var sql = new StringBuilder($"CREATE OR REPLACE TABLE {Quote(target)} AS SELECT * FROM {Quote(source.Name)}");
        var conditions = new List<string>();
        if (!string.IsNullOrWhiteSpace(search) && source.Columns.Count > 0)
        {
            string pattern = "%" + search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            conditions.Add(string.Join(" OR ", source.Columns.Select(c => $"CAST({Quote(c.Name)} AS VARCHAR) ILIKE {Literal(pattern)} ESCAPE '\\'")));
        }
        foreach (var filter in filters)
        {
            string type = source.Columns.FirstOrDefault(c => c.Name == filter.Column)?.Type ?? "VARCHAR";
            conditions.Add(FilterSql(filter, type));
        }
        if (conditions.Count > 0)
        {
            sql.Append(" WHERE ");
            sql.Append(string.Join(" AND ", conditions.Select(c => "(" + c + ")")));
        }
        if (sortColumn is not null)
        {
            sql.Append($" ORDER BY {Quote(sortColumn)} {(descending ? "DESC" : "ASC")} NULLS LAST");
        }
        Execute(connection, sql.ToString());
        return Describe(connection, target, isTable: true);
    });

    /// <summary>Süzgecin WHERE koşulu. Karşılaştırmalar kolon türüne göre kurulur: tarih kolonunda tarih, sayıya benzeyen değerde sayı, yoksa metin.</summary>
    private static string FilterSql(DataFilter filter, string columnType)
    {
        string column = Quote(filter.Column);
        string text = $"CAST({column} AS VARCHAR)";
        string value = filter.Value ?? string.Empty;

        string Like(string pattern)
        {
            string escaped = value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            string match = $"{text} ILIKE {Literal(string.Format(CultureInfo.InvariantCulture, pattern, escaped))} ESCAPE '\\'";
            return filter.Exclude ? $"{column} IS NULL OR NOT ({match})" : match;
        }

        string Compare(string op, string raw)
        {
            raw = raw.Trim();
            bool temporal = columnType.StartsWith("DATE", StringComparison.OrdinalIgnoreCase) ||
                            columnType.StartsWith("TIME", StringComparison.OrdinalIgnoreCase);
            if (temporal) return $"{column} {op} TRY_CAST({Literal(raw)} AS {columnType})";
            if (TryNumber(raw, out double number))
            {
                return $"TRY_CAST({column} AS DOUBLE) {op} {number.ToString("R", CultureInfo.InvariantCulture)}";
            }
            return $"{text} {op} {Literal(raw)}";
        }

        switch (filter.Op)
        {
            case FilterOp.Contains: return Like("%{0}%");
            case FilterOp.StartsWith: return Like("{0}%");
            case FilterOp.EndsWith: return Like("%{0}");
            case FilterOp.Greater: return Compare(">", value);
            case FilterOp.GreaterOrEqual: return Compare(">=", value);
            case FilterOp.Less: return Compare("<", value);
            case FilterOp.LessOrEqual: return Compare("<=", value);
            case FilterOp.Between: return "(" + Compare(">=", value) + ") AND (" + Compare("<=", filter.Value2 ?? value) + ")";
            case FilterOp.Empty:
                return filter.Exclude
                    ? $"{column} IS NOT NULL AND TRIM({text}) <> ''"
                    : $"{column} IS NULL OR TRIM({text}) = ''";
            default:
                if (filter.Value is null) return filter.Exclude ? $"{column} IS NOT NULL" : $"{column} IS NULL";
                return filter.Exclude ? $"{column} IS NULL OR {text} <> {Literal(filter.Value)}" : $"{text} = {Literal(filter.Value)}";
        }
    }

    /// <summary>"1250", "1250.5" ve "1250,5" sayı sayılır (binlik ayraçlı yazımlar sayılmaz).</summary>
    private static bool TryNumber(string raw, out double number)
    {
        string normalized = raw.Contains(',') && !raw.Contains('.') ? raw.Replace(',', '.') : raw;
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);
    }

    /// <summary>Kullanıcının SQL'ini çalıştırır ve sonucu tabloya alır. Yalnızca sonuç döndüren ifadeler (SELECT, SUMMARIZE, DESCRIBE…) çalışır.</summary>
    public Task<DataRelation> QueryAsync(string sql, string target) => RunAsync(connection =>
    {
        string body = sql.Trim().TrimEnd(';').Trim();
        if (body.Length == 0) throw new DataEngineException(Loc.T("Sorgu boş."));
        Execute(connection, $"CREATE OR REPLACE TABLE {Quote(target)} AS SELECT * FROM (\n{body}\n)");
        return Describe(connection, target, isTable: true);
    });

    public Task<ColumnProfile> ProfileAsync(DataRelation relation, DataColumn column) => RunAsync(connection =>
    {
        string c = Quote(column.Name);
        string from = " FROM " + Quote(relation.Name);
        long rows = relation.RowCount, nonNull = 0, distinct = 0;
        string? min = null, max = null, average = null, median = null;

        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT COUNT({c}), approx_count_distinct({c})" + from;
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                nonNull = Convert.ToInt64(reader.GetValue(0), CultureInfo.InvariantCulture);
                distinct = Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture);
            }
        }

        try
        {
            // İç içe türlerde (liste, harita) en küçük/en büyük tanımlı olmayabilir; o zaman boş kalır.
            string extra = column.IsNumeric ? $", CAST(AVG({c}) AS VARCHAR), CAST(MEDIAN({c}) AS VARCHAR)" : string.Empty;
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT CAST(MIN({c}) AS VARCHAR), CAST(MAX({c}) AS VARCHAR){extra}" + from;
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                min = ReadText(reader, 0);
                max = ReadText(reader, 1);
                if (column.IsNumeric)
                {
                    average = ReadText(reader, 2);
                    median = ReadText(reader, 3);
                }
            }
        }
        catch (Exception)
        {
            min = max = average = median = null;
        }

        var top = new List<(string? Value, long Count)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT CAST({c} AS VARCHAR) AS v, COUNT(*) AS n" + from + " GROUP BY 1 ORDER BY n DESC, v LIMIT 12";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                top.Add((ReadText(reader, 0), Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture)));
            }
        }
        return new ColumnProfile(column, rows, nonNull, distinct, min, max, average, median, top);
    });

    public Task ExportAsync(DataRelation relation, string path, ExportFormat format) => RunAsync(connection =>
    {
        string options = format switch
        {
            ExportFormat.Tsv => "FORMAT CSV, HEADER, DELIMITER '\t'",
            ExportFormat.Parquet => "FORMAT PARQUET",
            ExportFormat.Json => "FORMAT JSON, ARRAY true",
            ExportFormat.JsonLines => "FORMAT JSON",
            _ => "FORMAT CSV, HEADER",
        };
        Execute(connection, $"COPY (SELECT * FROM {Quote(relation.Name)}) TO {Literal(path)} ({options})");
        return true;
    });

    /// <summary>
    /// Veriyi Excel dosyasına yazar. Sayılar sayı, tarihler tarih, diğer her şey metin olur.
    /// Excel 15 basamaktan uzun tamsayıları yuvarladığı için öyle kolonlar (ör. uzun kimlikler) metin yazılır.
    /// Dönen değer yazılan veri satırı sayısı ve sayfa sınırı aşıldığı için verinin kesilip kesilmediğidir.
    /// </summary>
    public Task<(long Rows, bool Truncated)> ExportXlsxAsync(DataRelation relation, string path) => RunAsync(connection =>
    {
        // Her kolon için: 0 metin, 1 sayı, 2 tarih, 3 tarih + saat, 4 mantıksal
        var kinds = new int[relation.Columns.Count];
        var select = new List<string>();
        for (int i = 0; i < relation.Columns.Count; i++)
        {
            var column = relation.Columns[i];
            string c = Quote(column.Name), type = column.Type.ToUpperInvariant();
            if (column.IsNumeric)
            {
                bool wide = type.StartsWith("BIGINT") || type.StartsWith("UBIGINT") || type.StartsWith("HUGEINT") || type.StartsWith("UHUGEINT") || type.StartsWith("DECIMAL");
                if (wide && ExceedsExcelPrecision(connection, relation.Name, column.Name))
                {
                    select.Add($"CAST({c} AS VARCHAR)");
                }
                else
                {
                    kinds[i] = 1;
                    select.Add($"CAST({c} AS DOUBLE)");
                }
            }
            else if (type == "DATE")
            {
                kinds[i] = 2;
                select.Add($"strftime({c}, '%Y-%m-%d')");
            }
            else if (type is "TIMESTAMP" or "TIMESTAMP_S" or "TIMESTAMP_MS" or "TIMESTAMP_NS" or "DATETIME")
            {
                kinds[i] = 3;
                select.Add($"strftime({c}, '%Y-%m-%d %H:%M:%S')");
            }
            else if (type == "BOOLEAN")
            {
                kinds[i] = 4;
                select.Add($"CAST({c} AS VARCHAR)");
            }
            else
            {
                select.Add($"CAST({c} AS VARCHAR)");
            }
        }

        long rows = 0;
        bool truncated = false;
        using (var writer = new XlsxWriter(path, relation.Name.StartsWith("__zdp_", StringComparison.Ordinal) ? "Veri" : relation.Name))
        {
            writer.WriteHeader(relation.Columns.Select(col => col.Name).ToList());
            if (select.Count > 0)
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT {string.Join(", ", select)} FROM {Quote(relation.Name)}" + (relation.IsTable ? " ORDER BY rowid" : string.Empty);
                using var reader = command.ExecuteReader();
                var values = new object?[select.Count];
                while (reader.Read())
                {
                    for (int i = 0; i < values.Length; i++)
                    {
                        if (reader.IsDBNull(i))
                        {
                            values[i] = null;
                            continue;
                        }
                        object raw = reader.GetValue(i);
                        string text = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
                        values[i] = kinds[i] switch
                        {
                            1 => raw is double number ? number : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : text,
                            2 => DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : text,
                            3 => DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var stamp) ? stamp : text,
                            4 => text == "true" ? true : text == "false" ? false : text,
                            _ => text,
                        };
                    }
                    if (!writer.WriteRow(values))
                    {
                        truncated = true;
                        break;
                    }
                    rows++;
                }
            }
        }
        return (rows, truncated);
    });

    /// <summary>Kolonda Excel'in tam gösteremeyeceği kadar büyük (15 basamaktan uzun) değer var mı.</summary>
    private static bool ExceedsExcelPrecision(DbConnection connection, string relation, string column)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COALESCE(MAX(ABS(CAST({Quote(column)} AS DOUBLE))), 0) >= 1e15 FROM {Quote(relation)}";
            return Convert.ToBoolean(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return true; // emin olunamıyorsa metin yaz: değer bozulmasın
        }
    }

    /// <summary>Tabloyu ya da görünümü siler; dosyanın kendi tablosuysa adını da serbest bırakır.</summary>
    public Task DropAsync(DataRelation relation) => RunAsync(connection =>
    {
        try
        {
            Execute(connection, $"DROP {(relation.IsTable ? "TABLE" : "VIEW")} IF EXISTS {Quote(relation.Name)}");
        }
        finally
        {
            ReleaseName(relation.Name);
        }
        return true;
    });

    // ------------------------------------------------------------------ altyapı

    private async Task<T> RunAsync<T>(Func<DbConnection, T> work)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                try
                {
                    return work(EnsureOpen());
                }
                catch (DataEngineException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    throw new DataEngineException(CleanMessage(ex.Message), ex);
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string TempFolder
    {
        get
        {
            string folder = Path.Combine(Path.GetTempPath(), "ZeroDataPeek");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    private DbConnection EnsureOpen()
    {
        if (_connection is not null) return _connection;

        CleanStale();
        _databasePath = Path.Combine(TempFolder, "session-" + Guid.NewGuid().ToString("N") + ".duckdb");
        var connection = new DuckDBConnection("Data Source=" + _databasePath);
        connection.Open();
        // Uygulama internete bağlanmaz: eksik eklentiler sessizce indirilmesin.
        TryExecute(connection, "SET autoinstall_known_extensions=false");
        TryExecute(connection, "SET autoload_known_extensions=false");
        _connection = connection;
        return connection;
    }

    /// <summary>Önceki oturumlardan kalan geçici dosyaları siler (uygulama tek örnek çalıştığı için hepsi eskidir).</summary>
    private static void CleanStale()
    {
        try
        {
            foreach (var file in Directory.GetFiles(TempFolder))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception)
                {
                    // kullanımda: bırak
                }
            }
        }
        catch (Exception)
        {
            // geçici klasöre erişilemiyor: önemli değil
        }
    }

    private static void Execute(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void TryExecute(DbConnection connection, string sql)
    {
        try
        {
            Execute(connection, sql);
        }
        catch (Exception ex)
        {
            CrashLog.Write("DuckDB ayarı", ex);
        }
    }

    private static DataRelation Describe(DbConnection connection, string name, bool isTable)
    {
        var columns = new List<DataColumn>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DESCRIBE " + Quote(name);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(new DataColumn(ReadText(reader, 0) ?? string.Empty, ReadText(reader, 1) ?? string.Empty));
            }
        }

        long rows;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT COUNT(*) FROM " + Quote(name);
            rows = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        return new DataRelation(name, isTable, columns, rows);
    }

    private static string? ReadText(DbDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture);

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static string Literal(string value) => "'" + value.Replace("'", "''") + "'";

    /// <summary>DuckDB hata metnindeki iç tablo adlarını ve gereksiz boşlukları temizler.</summary>
    private static string CleanMessage(string message) => message.Trim();

    private static string SafeExtension(string? path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        try
        {
            return Path.GetExtension(path).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    /// <summary>Dosya adından SQL'de tırnaksız yazılabilen, benzersiz bir tablo adı üretir.</summary>
    private string ReserveName(string path)
    {
        var builder = new StringBuilder();
        foreach (char raw in Path.GetFileNameWithoutExtension(path).ToLowerInvariant())
        {
            char c = raw switch
            {
                'ı' or '\u0130' => 'i',
                'ş' => 's',
                'ğ' => 'g',
                'ü' => 'u',
                'ö' => 'o',
                'ç' => 'c',
                _ => raw,
            };
            builder.Append((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ? c : '_');
        }
        string name = builder.ToString().Trim('_');
        while (name.Contains("__", StringComparison.Ordinal)) name = name.Replace("__", "_", StringComparison.Ordinal);
        if (name.Length == 0) name = "tablo";
        if (name.Length > 48) name = name[..48].Trim('_');
        if (char.IsDigit(name[0])) name = "t_" + name;
        if (Reserved.Contains(name)) name += "_tablo";

        lock (_names)
        {
            string unique = name;
            for (int i = 2; _names.Contains(unique); i++) unique = name + "_" + i.ToString(CultureInfo.InvariantCulture);
            _names.Add(unique);
            return unique;
        }
    }

    private void ReleaseName(string name)
    {
        lock (_names) _names.Remove(name);
    }

    /// <summary>Bağlantıyı kapatır ve geçici veritabanını siler (uygulama kapanırken).</summary>
    public void Dispose()
    {
        try
        {
            _connection?.Dispose();
        }
        catch (Exception ex)
        {
            CrashLog.Write("DuckDB kapanış", ex);
        }
        _connection = null;

        var files = new List<string>();
        if (_databasePath is not null)
        {
            files.Add(_databasePath);
            files.Add(_databasePath + ".wal");
        }
        lock (_tempFiles) files.AddRange(_tempFiles);
        foreach (var file in files)
        {
            try
            {
                if (File.Exists(file)) File.Delete(file);
            }
            catch (Exception)
            {
                // bir sonraki açılışta temizlenir
            }
        }
    }
}
