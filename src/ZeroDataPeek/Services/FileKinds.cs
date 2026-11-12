namespace ZeroDataPeek.Services;

/// <summary>Bir sekmenin hangi modda açılacağı. İlk sürümde tüm metin dosyaları düz metin düzenleyicide açılır.</summary>
public enum DocumentKind
{
    Text,
    Code,
    Data,
    Document,
}

/// <summary>Uzantıdan dosya türü, dil adı ve simge.</summary>
public static class FileKinds
{
    private static readonly Dictionary<string, string> Languages = new(StringComparer.OrdinalIgnoreCase)
    {
        [".py"] = "Python", [".pyw"] = "Python", [".ipynb"] = "Jupyter Notebook", [".r"] = "R", [".sql"] = "SQL",
        [".js"] = "JavaScript", [".mjs"] = "JavaScript", [".cjs"] = "JavaScript", [".jsx"] = "JavaScript JSX",
        [".ts"] = "TypeScript", [".tsx"] = "TypeScript JSX",
        [".html"] = "HTML", [".htm"] = "HTML", [".css"] = "CSS", [".scss"] = "SCSS", [".less"] = "Less",
        [".cs"] = "C#", [".vb"] = "Visual Basic", [".fs"] = "F#", [".xaml"] = "XAML",
        [".java"] = "Java", [".kt"] = "Kotlin", [".kts"] = "Kotlin", [".scala"] = "Scala", [".groovy"] = "Groovy",
        [".c"] = "C", [".h"] = "C/C++", [".cpp"] = "C++", [".cc"] = "C++", [".hpp"] = "C++",
        [".go"] = "Go", [".rs"] = "Rust", [".swift"] = "Swift", [".dart"] = "Dart", [".php"] = "PHP", [".rb"] = "Ruby",
        [".lua"] = "Lua", [".pl"] = "Perl", [".jl"] = "Julia", [".m"] = "Power Query M", [".pq"] = "Power Query M", [".dax"] = "DAX",
        [".ps1"] = "PowerShell", [".psm1"] = "PowerShell", [".bat"] = "Batch", [".cmd"] = "Batch", [".sh"] = "Shell", [".bash"] = "Shell",
        [".xml"] = "XML", [".xsd"] = "XML", [".xslt"] = "XSLT", [".svg"] = "SVG", [".csproj"] = "XML", [".props"] = "XML",
        [".yaml"] = "YAML", [".yml"] = "YAML", [".toml"] = "TOML", [".ini"] = "INI", [".cfg"] = "INI", [".conf"] = "Config",
        [".env"] = "Env", [".dockerfile"] = "Dockerfile", [".tf"] = "Terraform", [".gradle"] = "Gradle",
        [".tex"] = "LaTeX", [".diff"] = "Diff", [".patch"] = "Diff", [".vbs"] = "VBScript", [".asm"] = "Assembly",
    };

    private static readonly Dictionary<string, string> DataFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        [".csv"] = "CSV", [".tsv"] = "TSV", [".tab"] = "TSV", [".json"] = "JSON", [".jsonl"] = "JSON Lines", [".ndjson"] = "JSON Lines",
        [".parquet"] = "Parquet", [".xlsx"] = "Excel", [".xls"] = "Excel", [".xlsm"] = "Excel", [".avro"] = "Avro", [".orc"] = "ORC",
        [".feather"] = "Feather", [".arrow"] = "Arrow", [".duckdb"] = "DuckDB", [".sqlite"] = "SQLite", [".db"] = "SQLite",
    };

    private static readonly Dictionary<string, string> DocumentFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        [".rtf"] = "RTF", [".docx"] = "Word", [".doc"] = "Word", [".odt"] = "OpenDocument", [".pdf"] = "PDF",
        [".md"] = "Markdown", [".markdown"] = "Markdown",
    };

    /// <summary>Metin olarak açılamayan, kendi modunu bekleyen biçimler.</summary>
    private static readonly HashSet<string> BinaryFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        ".parquet", ".xlsx", ".xls", ".xlsm", ".avro", ".orc", ".feather", ".arrow", ".duckdb", ".sqlite", ".db",
        ".docx", ".doc", ".odt", ".pdf",
    };

    public static DocumentKind KindOf(string? path)
    {
        var ext = Extension(path);
        if (DataFormats.ContainsKey(ext)) return DocumentKind.Data;
        if (DocumentFormats.ContainsKey(ext)) return DocumentKind.Document;
        if (Languages.ContainsKey(ext)) return DocumentKind.Code;
        return DocumentKind.Text;
    }

    /// <summary>Durum çubuğunda gösterilen biçim ya da dil adı.</summary>
    public static string FormatName(string? path)
    {
        var ext = Extension(path);
        if (DataFormats.TryGetValue(ext, out var data)) return data;
        if (DocumentFormats.TryGetValue(ext, out var doc)) return doc;
        if (Languages.TryGetValue(ext, out var language)) return language;
        if (ext.Equals(".log", StringComparison.OrdinalIgnoreCase)) return Loc.T("Günlük");
        return Loc.T("Düz metin");
    }

    public static bool IsBinaryFormat(string? path) => BinaryFormats.Contains(Extension(path));

    /// <summary>Uzantısız ya da noktayla başlayan, metin olduğu bilinen dosya adları.</summary>
    private static readonly HashSet<string> TextFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "readme", "license", "licence", "notice", "authors", "changelog", "makefile", "dockerfile", "gemfile", "procfile",
        ".gitignore", ".gitattributes", ".editorconfig", ".env", ".npmrc", ".dockerignore", ".prettierrc", ".eslintrc",
    };

    private static readonly HashSet<string> PlainTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".text", ".nfo", ".srt", ".vtt", ".lst", ".out", ".err", ".properties", ".reg", ".url", ".plist",
        ".sln", ".map", ".lock", ".rst", ".adoc", ".org", ".bib",
    };

    /// <summary>
    /// Uygulamanın bu dosyayı açıp açamayacağı (gezgin panelinde açılamayanlar soluk gösterilir).
    /// Veri motorunun desteklediği biçimler ya da metin olduğu uzantısından bilinen dosyalar açılabilir.
    /// </summary>
    public static bool CanOpen(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (DataEngine.Supports(path)) return true;
        string ext = Extension(path);
        if (BinaryFormats.Contains(ext)) return false;
        if (Languages.ContainsKey(ext) || DataFormats.ContainsKey(ext) || DocumentFormats.ContainsKey(ext) || PlainTextExtensions.Contains(ext)) return true;
        try
        {
            return TextFileNames.Contains(Path.GetFileName(path));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Segoe Fluent Icons simgesi.</summary>
    public static string Glyph(DocumentKind kind) => kind switch
    {
        DocumentKind.Code => "",
        DocumentKind.Data => "",
        DocumentKind.Document => "",
        _ => "",
    };

    public static string ModeName(DocumentKind kind) => kind switch
    {
        DocumentKind.Code => Loc.T("Kod"),
        DocumentKind.Data => Loc.T("Veri"),
        DocumentKind.Document => Loc.T("Belge"),
        _ => Loc.T("Metin"),
    };

    private static string Extension(string? path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        try
        {
            var name = Path.GetFileName(path);
            if (name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase)) return ".dockerfile";
            return Path.GetExtension(name);
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }
}
