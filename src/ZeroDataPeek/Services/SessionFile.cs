using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZeroDataPeek.Services;

/// <summary>Oturumdaki bir sekme: dosya yolu ve hangi görünümde açık olduğu ("text", "data" ya da "document").</summary>
public sealed class SessionTab
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "text";
}

/// <summary>
/// Adlandırılmış oturum: birlikte açılan dosyaların listesi. ".zdp" uzantılı, elle de okunabilen bir JSON dosyasıdır.
/// Dosyaların kendisini değil yollarını tutar; taşınan ya da silinen dosyalar açılışta bildirilir.
/// </summary>
public sealed class SessionFile
{
    public const string Extension = ".zdp";
    private const string FormatName = "zero-data-peek-session";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [JsonPropertyName("format")]
    public string Format { get; set; } = FormatName;

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("saved")]
    public string Saved { get; set; } = string.Empty;

    /// <summary>Oturum kaydedilirken etkin olan sekmenin yolu.</summary>
    [JsonPropertyName("active")]
    public string Active { get; set; } = string.Empty;

    [JsonPropertyName("tabs")]
    public List<SessionTab> Tabs { get; set; } = new();

    public static bool IsSession(string? path)
    {
        try
        {
            return string.Equals(System.IO.Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Oturum dosyasını okur. Biçim tanınmıyorsa InvalidDataException fırlatır.</summary>
    public static SessionFile Load(string path)
    {
        SessionFile? session;
        try
        {
            session = JsonSerializer.Deserialize<SessionFile>(File.ReadAllText(path), Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(Loc.T("Bu dosya geçerli bir Zero Data Peek oturumu değil."), ex);
        }
        if (session is null || session.Format != FormatName)
        {
            throw new InvalidDataException(Loc.T("Bu dosya geçerli bir Zero Data Peek oturumu değil."));
        }
        session.Tabs = (session.Tabs ?? new List<SessionTab>()).Where(t => !string.IsNullOrWhiteSpace(t.Path)).ToList();
        if (string.IsNullOrWhiteSpace(session.Name)) session.Name = System.IO.Path.GetFileNameWithoutExtension(path);
        return session;
    }

    public void Save(string path)
    {
        Name = System.IO.Path.GetFileNameWithoutExtension(path);
        Saved = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options), new System.Text.UTF8Encoding(false));
    }
}
