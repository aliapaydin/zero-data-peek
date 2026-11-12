using System.Globalization;
using System.Text;

namespace ZeroDataPeek.Services;

/// <summary>Kullanıcının seçebildiği bir metin kodlaması.</summary>
public sealed class EncodingChoice
{
    private readonly Func<Encoding> _create;

    public EncodingChoice(string id, string name, Func<Encoding> create)
    {
        Id = id;
        Name = name;
        _create = create;
    }

    public string Id { get; }
    public string Name { get; }

    /// <summary>Yazarken kullanılacak kodlayıcı (BOM bilgisi kodlayıcının içindedir).</summary>
    public Encoding Create() => _create();
}

/// <summary>Okunan metin dosyası: içerik, algılanan kodlama ve satır sonu.</summary>
public sealed record LoadedText(string Text, EncodingChoice Encoding, string Eol);

/// <summary>Metin dosyalarını kodlama ve satır sonu biçimini koruyarak okur ve yazar.</summary>
public static class TextFileIO
{
    /// <summary>Düz metin düzenleyicinin açtığı en büyük dosya.</summary>
    public const long MaxTextBytes = 8 * 1024 * 1024;

    public static readonly EncodingChoice Utf8 = new("utf-8", "UTF-8", () => new UTF8Encoding(false));
    public static readonly EncodingChoice Utf8Bom = new("utf-8-bom", "UTF-8 BOM", () => new UTF8Encoding(true));
    public static readonly EncodingChoice Utf16Le = new("utf-16le", "UTF-16 LE", () => new UnicodeEncoding(false, true));
    public static readonly EncodingChoice Utf16Be = new("utf-16be", "UTF-16 BE", () => new UnicodeEncoding(true, true));
    public static readonly EncodingChoice Windows1254 = new("windows-1254", "Windows-1254 (Türkçe)", () => Encoding.GetEncoding(1254));
    public static readonly EncodingChoice Windows1252 = new("windows-1252", "Windows-1252 (Batı Avrupa)", () => Encoding.GetEncoding(1252));
    public static readonly EncodingChoice Iso88599 = new("iso-8859-9", "ISO-8859-9 (Türkçe)", () => Encoding.GetEncoding(28599));

    public static IReadOnlyList<EncodingChoice> Choices { get; } = new[]
    {
        Utf8, Utf8Bom, Utf16Le, Utf16Be, Windows1254, Windows1252, Iso88599,
    };

    /// <summary>
    /// Dosyayı okur. <paramref name="forced"/> verilmezse kodlama algılanır: önce BOM, sonra geçerli UTF-8,
    /// olmazsa Windows'un ANSI kod sayfası. İkili dosya ise null döner.
    /// </summary>
    public static LoadedText? Load(string path, EncodingChoice? forced = null)
    {
        byte[] bytes = File.ReadAllBytes(path);
        int skip = BomLength(bytes, out var bomChoice);

        EncodingChoice choice;
        if (forced is not null)
        {
            choice = forced;
            if (bomChoice is null || !SameFamily(bomChoice, forced)) skip = 0;
        }
        else if (bomChoice is not null)
        {
            choice = bomChoice;
        }
        else if (LooksUtf16Le(bytes))
        {
            choice = Utf16Le;
        }
        else
        {
            if (HasNul(bytes)) return null;
            choice = IsValidUtf8(bytes) ? Utf8 : AnsiChoice();
        }

        string text = choice.Create().GetString(bytes, skip, bytes.Length - skip);
        return new LoadedText(text, choice, DetectEol(text));
    }

    /// <summary>Metni seçilen kodlama ve satır sonuyla yazar. Kodlama bir karakteri yazamıyorsa EncoderFallbackException fırlatır.</summary>
    public static void Save(string path, string text, EncodingChoice choice, string eol)
    {
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (eol != "\n") normalized = normalized.Replace("\n", eol);

        var writer = choice.Create();
        // Yazılamayan karakter sessizce "?" olmasın: hata verip kullanıcıya başka kodlama seçtir.
        var strict = Encoding.GetEncoding(writer.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);
        byte[] body = strict.GetBytes(normalized);
        byte[] preamble = writer.GetPreamble();

        string full = Path.GetFullPath(path);
        string temp = full + ".zdp-tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(preamble, 0, preamble.Length);
                stream.Write(body, 0, body.Length);
            }
            File.Move(temp, full, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Klasöre geçici dosya yazılamıyor ya da yer değiştirilemiyor: doğrudan üzerine yaz.
            TryDelete(temp);
            using var stream = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None);
            stream.Write(preamble, 0, preamble.Length);
            stream.Write(body, 0, body.Length);
        }
    }

    public static string EolName(string eol) => eol switch
    {
        "\n" => "LF",
        "\r" => "CR",
        _ => "CRLF",
    };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
            // geçici dosya kalabilir; zararsız
        }
    }

    private static int BomLength(byte[] bytes, out EncodingChoice? choice)
    {
        choice = null;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            choice = Utf8Bom;
            return 3;
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            choice = Utf16Le;
            return 2;
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            choice = Utf16Be;
            return 2;
        }
        return 0;
    }

    private static bool SameFamily(EncodingChoice a, EncodingChoice b)
    {
        if (a == b) return true;
        return (a == Utf8Bom && b == Utf8) || (a == Utf8 && b == Utf8Bom);
    }

    private static bool HasNul(byte[] bytes)
    {
        int limit = Math.Min(bytes.Length, 8192);
        for (int i = 0; i < limit; i++)
        {
            if (bytes[i] == 0) return true;
        }
        return false;
    }

    /// <summary>BOM'suz UTF-16 LE: Latin ağırlıklı metinde tek konumlardaki baytların çoğu sıfırdır.</summary>
    private static bool LooksUtf16Le(byte[] bytes)
    {
        int limit = Math.Min(bytes.Length, 4096) & ~1;
        if (limit < 8) return false;
        int oddZeros = 0, evenZeros = 0;
        for (int i = 0; i < limit; i += 2)
        {
            if (bytes[i] == 0) evenZeros++;
            if (bytes[i + 1] == 0) oddZeros++;
        }
        int pairs = limit / 2;
        return oddZeros > pairs * 0.6 && evenZeros < pairs * 0.05;
    }

    private static bool IsValidUtf8(byte[] bytes)
    {
        try
        {
            new UTF8Encoding(false, true).GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>UTF-8 olmayan BOM'suz dosyalar için Windows'un ANSI kod sayfası.</summary>
    private static EncodingChoice AnsiChoice()
    {
        int codePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
        if (codePage == 1254) return Windows1254;
        if (codePage == 1252 || codePage <= 0) return Windows1252;
        try
        {
            var encoding = Encoding.GetEncoding(codePage);
            return new EncodingChoice("cp-" + codePage.ToString(CultureInfo.InvariantCulture), encoding.WebName, () => Encoding.GetEncoding(codePage));
        }
        catch (Exception)
        {
            return Windows1252;
        }
    }

    private static string DetectEol(string text)
    {
        int crlf = 0, lf = 0, cr = 0;
        int limit = Math.Min(text.Length, 1_000_000);
        for (int i = 0; i < limit; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (c == '\n')
            {
                lf++;
            }
        }
        if (lf > crlf && lf >= cr) return "\n";
        if (cr > crlf && cr > lf) return "\r";
        return "\r\n";
    }
}
