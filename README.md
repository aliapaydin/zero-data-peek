<p align="center">
  <img src="assets/brand/zdp-logo-256.png" width="112" alt="Zero Data Peek logosu">
</p>

<h1 align="center">Zero Data Peek</h1>

<p align="center">
  Veri, kod ve belge dosyalarını tek pencerede açan sekmeli görüntüleyici ve düzenleyici.<br>
  <sub>A tabbed viewer and editor for Windows that opens data, code and document files in one window.</sub>
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/lisans-MIT-1f6fd6" alt="MIT Lisansı"></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-1f6fd6" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/sürüm-0.1.0-1f6fd6" alt="Sürüm 0.1.0">
  <img src="https://img.shields.io/badge/.NET-10-512bd4" alt=".NET 10">
</p>

---

**Zero Data Peek**, [Zero File Commander](https://github.com/aliapaydin/zero-file-commander) ve [Zero File Extractor](https://github.com/aliapaydin/Zero-File-Extractor) ile aynı görsel dili paylaşan açık kaynaklı bir uygulama. Hedef: bir CSV'ye, Parquet dosyasına, Python betiğine ya da bir belgeye bakmak için dört ayrı program açmamak. Reklam, telemetri ve internet bağlantısı yok.

## Durum

Uygulama aşama aşama geliştiriliyor. Şu an **1. aşama (kabuk)** tamam; sıradaki aşamalar [yol haritasında](docs/ROADMAP.md).

| Aşama | İçerik | Durum |
|---|---|---|
| 1 | Kabuk: sekmeler, tema, düz metin düzenleyici, komut paleti | Tamam |
| 2 | Veri modu: CSV, Parquet, JSON, Excel; tablo, kolon profili, SQL (DuckDB) | Sırada |
| 3 | Kod modu: sözdizimi renklendirme, bul/değiştir, kod katlama | Planlandı |
| 4 | Belge modu: zengin metin, RTF/DOCX/PDF | Planlandı |
| 5 | `.zdp` çalışma paketi: veri, sorgular ve notlar tek dosyada | Planlandı |

## Bugün neler var

- **Yatay ve dikey sekmeler:** sekmeler üstte başlık çubuğunda ya da solda boyutlandırılabilir bir listede. Sürükleyerek sıralanır, `Ctrl+Shift+L` ile düzen değişir.
- **Düz metin düzenleyici:** kodlamayı (UTF-8, UTF-16, Windows-1254, …) ve satır sonunu (CRLF, LF, CR) algılar, kaydederken korur. İkisi de durum çubuğundan değiştirilebilir.
- **Zarar vermez:** ikili dosyaları metin olarak açmaz; seçilen kodlama bir karakteri yazamıyorsa sessizce bozmak yerine uyarır; kaydetme önce geçici dosyaya yapılır.
- **Tek pencere:** Dosya Gezgini'nden açılan dosyalar çalışan pencereye yeni sekme olarak gelir.
- **Oturum:** kapanışta açık olan dosyalar bir sonraki açılışta geri gelir.
- **Komut paleti:** `Ctrl+Shift+P` ile her komuta yazarak ulaş.
- **Açık, koyu ve sistem teması**, Mica zemin, Türkçe ve İngilizce arayüz.

## Klavye kısayolları

| Kısayol | Eylem |
|---|---|
| `Ctrl+N` / `Ctrl+T` | Yeni sekme |
| `Ctrl+O` | Dosya aç |
| `Ctrl+S` / `Ctrl+Shift+S` | Kaydet / Farklı kaydet |
| `Ctrl+W` / `Ctrl+Shift+W` | Sekmeyi kapat / Tüm sekmeleri kapat |
| `Ctrl+Tab` / `Ctrl+Shift+Tab` | Sonraki / önceki sekme |
| `Ctrl+Shift+P` | Komut paleti |
| `Ctrl+Shift+L` | Sekme düzenini değiştir (yatay / dikey) |
| `Alt+Z` | Sözcük kaydırma |
| `Ctrl++` / `Ctrl+-` / `Ctrl+0` | Yakınlaştır / uzaklaştır / sıfırla |
| `Ctrl+,` | Ayarlar |

## Derleme

Gerekenler: Windows 10 (2004) ya da üstü, Visual Studio 2022 ya da üstü, ".NET masaüstü geliştirme" ve "WinUI uygulama geliştirme" iş yükleri, .NET 10 SDK.

1. Depoyu klonla ve `ZeroDataPeek.sln` dosyasını aç.
2. Platformu **x64** (ya da ARM64) seç; başlangıç profili **ZeroDataPeek (Package)** olsun.
3. `F5`.

Her gönderimde GitHub Actions x64 ve ARM64 için derler (`.github/workflows/build.yml`).

## Yapı

```
src/ZeroDataPeek/     WinUI 3 uygulaması
  Models/             Sekme modeli
  Services/           Ayarlar, dil, dosya türleri, metin okuma/yazma, pencere yardımcıları
  Views/              Düzenleyici, bilgi sayfası, diyaloglar
  Controls/           Panel ayırıcı
  Strings/en.json     İngilizce metinler
tools/                Çeviri denetimi
docs/                 Tasarım, yol haritası, yayımlama
```

## Lisans

[MIT](LICENSE) © Ali Apaydın

## Gizlilik

Uygulama veri toplamaz ve internete bağlanmaz. Ayrıntılar: [PRIVACY.md](PRIVACY.md)
