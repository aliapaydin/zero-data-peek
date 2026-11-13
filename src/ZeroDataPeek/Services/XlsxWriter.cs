using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace ZeroDataPeek.Services;

/// <summary>
/// Tek sayfalı, akış halinde yazan asgari XLSX yazıcısı (ek kitaplık gerektirmez).
/// Hücre değerleri üç türden biridir: double (sayı), DateTime (tarih biçimli sayı), string (satır içi metin); null boş hücredir.
/// Başlık satırı kalın yazılır, dondurulur ve otomatik süzgeç eklenir.
/// </summary>
public sealed class XlsxWriter : IDisposable
{
    /// <summary>Excel'in bir sayfada tuttuğu en çok satır (başlık dahil).</summary>
    public const int MaxRows = 1_048_576;

    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly DateTime Epoch = new(1899, 12, 30);
    private static readonly DateTime FirstSafeDate = new(1900, 3, 1); // öncesinde Excel'in 1900 artık yıl hatası var

    private readonly ZipArchive _zip;
    private readonly StreamWriter _sheet;
    private readonly StringBuilder _line = new();
    private int _columns;
    private int _row;
    private bool _finished;

    public XlsxWriter(string path, string sheetName)
    {
        _zip = new ZipArchive(new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None), ZipArchiveMode.Create);

        Write("[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
            "</Types>");
        Write("_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>");
        Write("xl/workbook.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"" + MainNs + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<sheets><sheet name=\"" + Escape(SafeSheetName(sheetName)) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
        Write("xl/_rels/workbook.xml.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "</Relationships>");
        // Hücre stilleri: 0 genel, 1 tarih, 2 tarih + saat, 3 kalın (başlık)
        Write("xl/styles.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<styleSheet xmlns=\"" + MainNs + "\">" +
            "<numFmts count=\"2\"><numFmt numFmtId=\"164\" formatCode=\"yyyy\\-mm\\-dd\"/><numFmt numFmtId=\"165\" formatCode=\"yyyy\\-mm\\-dd\\ hh:mm:ss\"/></numFmts>" +
            "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
            "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
            "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"4\">" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
            "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
            "</cellXfs></styleSheet>");

        _sheet = new StreamWriter(_zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest).Open(), new UTF8Encoding(false));
    }

    /// <summary>Yazılan satır sayısı (başlık dahil).</summary>
    public int RowCount => _row;

    /// <summary>Başlık satırını yazar; ilk çağrı olmalı.</summary>
    public void WriteHeader(IReadOnlyList<string> names)
    {
        _columns = names.Count;
        _sheet.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"" + MainNs + "\">");
        _sheet.Write("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        _sheet.Write("<sheetFormatPr defaultRowHeight=\"15\"/><cols>");
        for (int i = 0; i < names.Count; i++)
        {
            int width = Math.Clamp(names[i].Length + 4, 10, 40);
            _sheet.Write("<col min=\"" + Invariant(i + 1) + "\" max=\"" + Invariant(i + 1) + "\" width=\"" + Invariant(width) + "\" customWidth=\"1\"/>");
        }
        _sheet.Write("</cols><sheetData>");

        _row = 1;
        _line.Clear().Append("<row r=\"1\">");
        for (int i = 0; i < names.Count; i++) AppendText(i, names[i], style: 3);
        _sheet.Write(_line.Append("</row>").ToString());
    }

    /// <summary>Bir veri satırı yazar. Sayfa dolduysa false döner ve satır yazılmaz.</summary>
    public bool WriteRow(IReadOnlyList<object?> values)
    {
        if (_row >= MaxRows) return false;
        _row++;
        _line.Clear().Append("<row r=\"").Append(Invariant(_row)).Append("\">");
        for (int i = 0; i < values.Count && i < _columns; i++)
        {
            switch (values[i])
            {
                case null:
                    break;
                case double number when !double.IsNaN(number) && !double.IsInfinity(number):
                    AppendNumber(i, number, style: 0);
                    break;
                case DateTime date when date >= FirstSafeDate:
                    AppendNumber(i, (date - Epoch).TotalDays, style: date.TimeOfDay == TimeSpan.Zero ? 1 : 2);
                    break;
                case DateTime date:
                    AppendText(i, date.ToString(date.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), style: 0);
                    break;
                case bool flag:
                    _line.Append("<c r=\"").Append(Reference(i)).Append("\" t=\"b\"><v>").Append(flag ? '1' : '0').Append("</v></c>");
                    break;
                default:
                    AppendText(i, Convert.ToString(values[i], CultureInfo.InvariantCulture) ?? string.Empty, style: 0);
                    break;
            }
        }
        _sheet.Write(_line.Append("</row>").ToString());
        return true;
    }

    private void AppendNumber(int column, double value, int style)
    {
        _line.Append("<c r=\"").Append(Reference(column)).Append('"');
        if (style != 0) _line.Append(" s=\"").Append(Invariant(style)).Append('"');
        _line.Append("><v>").Append(value.ToString("R", CultureInfo.InvariantCulture)).Append("</v></c>");
    }

    private void AppendText(int column, string text, int style)
    {
        if (text.Length == 0) return;
        if (text.Length > 32767) text = text[..32767]; // Excel hücre sınırı
        _line.Append("<c r=\"").Append(Reference(column)).Append("\" t=\"inlineStr\"");
        if (style != 0) _line.Append(" s=\"").Append(Invariant(style)).Append('"');
        _line.Append("><is><t xml:space=\"preserve\">").Append(Escape(text)).Append("</t></is></c>");
    }

    private string Reference(int column) => ColumnName(column) + Invariant(_row);

    /// <summary>0 → A, 25 → Z, 26 → AA…</summary>
    public static string ColumnName(int index)
    {
        string name = string.Empty;
        for (int n = index + 1; n > 0; n = (n - 1) / 26) name = (char)('A' + (n - 1) % 26) + name;
        return name;
    }

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>XML'de geçersiz denetim karakterlerini atar, özel karakterleri kaçırır.</summary>
    private static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length + 8);
        foreach (char c in text)
        {
            switch (c)
            {
                case '&': builder.Append("&amp;"); break;
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                case '"': builder.Append("&quot;"); break;
                case '\t' or '\n' or '\r': builder.Append(c); break;
                default:
                    if (c >= 0x20 && c != 0xFFFE && c != 0xFFFF && !char.IsSurrogate(c)) builder.Append(c);
                    else if (char.IsSurrogate(c)) builder.Append(c); // eşli vekiller geçerlidir; tekil kalanlar aşağıda temizlenir
                    break;
            }
        }
        return RemoveLoneSurrogates(builder);
    }

    private static string RemoveLoneSurrogates(StringBuilder builder)
    {
        for (int i = builder.Length - 1; i >= 0; i--)
        {
            char c = builder[i];
            if (char.IsLowSurrogate(c))
            {
                if (i > 0 && char.IsHighSurrogate(builder[i - 1])) i--;
                else builder.Remove(i, 1);
            }
            else if (char.IsHighSurrogate(c))
            {
                builder.Remove(i, 1);
            }
        }
        return builder.ToString();
    }

    private static string SafeSheetName(string name)
    {
        var builder = new StringBuilder();
        foreach (char c in name)
        {
            builder.Append(c is '\\' or '/' or '?' or '*' or '[' or ']' or ':' ? '_' : c);
        }
        string result = builder.ToString().Trim('\'', ' ');
        if (result.Length == 0) result = "Veri";
        return result.Length > 31 ? result[..31] : result;
    }

    private void Write(string name, string content)
    {
        using var writer = new StreamWriter(_zip.CreateEntry(name, CompressionLevel.Fastest).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    /// <summary>Sayfayı kapatır ve dosyayı tamamlar.</summary>
    public void Dispose()
    {
        if (_finished) return;
        _finished = true;
        try
        {
            if (_row == 0) WriteHeader(Array.Empty<string>());
            _sheet.Write("</sheetData>");
            if (_columns > 0) _sheet.Write("<autoFilter ref=\"A1:" + ColumnName(_columns - 1) + Invariant(Math.Max(1, _row)) + "\"/>");
            _sheet.Write("</worksheet>");
        }
        finally
        {
            _sheet.Dispose();
            _zip.Dispose();
        }
    }
}
