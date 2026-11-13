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
  <img src="https://img.shields.io/badge/sürüm-1.0.0-1f6fd6" alt="Sürüm 1.0.0">
  <img src="https://img.shields.io/badge/.NET-10-512bd4" alt=".NET 10">
</p>

---

**Zero Data Peek**, [Zero File Commander](https://github.com/aliapaydin/zero-file-commander) ve [Zero File Extractor](https://github.com/aliapaydin/Zero-File-Extractor) ile aynı görsel dili paylaşan açık kaynaklı bir uygulama. Hedef: bir CSV'ye, Parquet dosyasına, Python betiğine ya da bir belgeye bakmak için dört ayrı program açmamak. Reklam, telemetri ve internet bağlantısı yok.

## Durum

Sürüm 1.0: ilk beş aşama tamam. Sonraki adımlar [yol haritasında](docs/ROADMAP.md).

| Aşama | İçerik | Durum |
|---|---|---|
| 1 | Kabuk: sekmeler, tema, düz metin düzenleyici, komut paleti | Tamam |
| 2 | Veri modu: CSV, Parquet, JSON, Excel; tablo, kolon profili, SQL (DuckDB) | Tamam |
| 3 | Kod modu ve gezgin: sözdizimi renklendirme, bul/değiştir, kod katlama, klasör paneli | Tamam |
| 4 | Belge modu (DOCX, PDF), oturumlar (`.zdp`), sağ tık menüsü | Tamam |
| 5 | Günlük kullanım: bildirimler, kompakt mod, bölünmüş görünüm, karşılaştırma, dosyalarda arama, koşullu süzgeçler | Tamam |
| 6 | `.zdp` çalışma paketi: süzgeçler ve veri de oturum dosyasında | Sırada |

## Bugün neler var

- **Bölünmüş görünüm (`Ctrl+Alt+B`):** iki sekme yan yana; örneğin solda SQL dosyası, sağda tablo. Ayraç sürüklenir, bölmeler yer değiştirir.
- **Dosya karşılaştırma (`Ctrl+Alt+D`):** iki dosyanın ya da açık iki sekmenin farkı, yan yana ya da satır içi; farklar arasında gezinme.
- **Dosyalarda ara (`Ctrl+Shift+F`):** bir klasördeki tüm metin dosyalarında arama; büyük/küçük harf, tam sözcük, düzenli ifade, dosya adı süzgeçleri. Sonuca tıklayınca dosya o satırda açılır.
- **Koşullu süzgeçler (`Ctrl+Alt+F`):** tabloda içerir, ile başlar, büyük, küçük, arasında, boş gibi koşullar; birden fazla koşul birlikte uygulanır, tek tek kaldırılır.
- **Kompakt mod (`Ctrl+Alt+K`):** alçak satırlar ve küçük yazılarla ekrana daha çok şey sığar.
- **Kaydedilmemiş sekmeler saklanır:** kapatırken sormaz, bir sonraki açılışta yazdıkların geri gelir. Diskte değişen dosya fark edilip yeniden yüklenir.
- **Biçimlendirme ve önizleme:** SQL, JSON ve XML'i tek tuşla biçimlendir (`Shift+Alt+F`); Markdown'ı yan yana önizle (`Ctrl+Shift+V`).
- **Belge modu:** Word (`.docx`) belgelerini A4 görünümlü zengin metin düzenleyicide aç, düzenle, kaydet; PDF, HTML ya da düz metin olarak dışa aktar, yazdır. `Ctrl+Shift+N` yeni belge açar.
- **Oturumlar:** açık sekmeleri adlandırılmış bir oturum olarak `.zdp` dosyasına kaydet (`Ctrl+Alt+S`); dosyayı açınca ya da çift tıklayınca sekmeler geri gelir.
- **Sağ tık menüsü:** Dosya Gezgini'nde "Zero Data Peek ile aç". Hangi dosya türlerinde görüneceğini Ayarlar'dan seçersin.
- **Dosya gezgini (`Ctrl+B`):** solda klasör ağacı. Açılabilen dosyalar normal, diğerleri soluk görünür; tıklayınca sekmede açılır.
- **Kod düzenleyici:** VS Code'un düzenleyicisi Monaco. 90 dile sözdizimi renklendirme (Python, SQL, DAX, Power Query, C#, JSON, YAML…), bul ve değiştir, kod katlama, çoklu imleç, küçük harita.
- **Veri modu:** CSV, TSV, Parquet, JSON, JSON Lines ve Excel dosyaları tablo olarak açılır. Hücreye sağ tıklayıp değere göre süzebilirsin. Satır sayısı ne olursa olsun akıcı kaydırma; başlığa tıklayıp sıralama, tüm kolonlarda arama.
- **SQL paneli (`Ctrl+E`):** gömülü DuckDB ile dosyayı sorgula. Her açık dosya bir tablodur; iki sekmeyi tablo adlarıyla birbirine join'leyebilirsin.
- **Kolon profili (`Ctrl+Shift+I`):** seçili kolonun dolu/boş oranı, tekil değer sayısı, en küçük/en büyük, ortalama, ortanca ve en sık değerleri.
- **Dışa aktarma (`Ctrl+Shift+E`):** görünen veriyi (sorgu, arama ve sıralama uygulanmış haliyle) CSV, TSV, Parquet ya da JSON olarak kaydet; yani biçimler arası dönüştürme.
- **Yatay ve dikey sekmeler:** sekmeler üstte başlık çubuğunda ya da solda boyutlandırılabilir bir listede. Sürükleyerek sıralanır, `Ctrl+Shift+L` ile düzen değişir.
- **Kodlama ve satır sonu:** düzenleyici kodlamayı (UTF-8, UTF-16, Windows-1254, …) ve satır sonunu (CRLF, LF, CR) algılar, kaydederken korur. İkisi de durum çubuğundan değiştirilebilir.
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
| `Ctrl+Shift+N` | Yeni belge |
| `Ctrl+Alt+S` | Oturumu kaydet |
| `Ctrl+P` | Yazdır (belge) |
| `Ctrl+B` / `Ctrl+Shift+O` | Gezgin paneli / Klasör aç |
| `Ctrl+F` / `Ctrl+H` / `Ctrl+G` | Bul / Değiştir / Satıra git |
| `Ctrl+E` | SQL paneli (içinde `Ctrl+Enter`: çalıştır) |
| `Ctrl+Shift+I` | Kolon profili |
| `Ctrl+Shift+E` | Dışa aktar |
| `Ctrl+C` / `Ctrl+Shift+C` | Tabloda hücreyi / satırı kopyala |
| `Alt+Z` | Sözcük kaydırma |
| `Ctrl++` / `Ctrl+-` / `Ctrl+0` | Yakınlaştır / uzaklaştır / sıfırla |
| `Ctrl+Alt+B` | Bölünmüş görünüm |
| `Ctrl+Alt+D` | Dosyaları karşılaştır |
| `Ctrl+Shift+F` | Dosyalarda ara |
| `Ctrl+Alt+F` | Koşullu süzgeç (veri sekmesi) |
| `Ctrl+Alt+K` | Kompakt mod |
| `Shift+Alt+F` / `Ctrl+Shift+V` | Belgeyi biçimlendir / Markdown önizleme |
| `Ctrl+,` | Ayarlar |

## Derleme

Gerekenler: Windows 10 (2004) ya da üstü, Visual Studio 2022 ya da üstü, ".NET masaüstü geliştirme", "WinUI uygulama geliştirme" ve "C++ ile masaüstü geliştirme" iş yükleri, .NET 10 SDK. C++ iş yükü yalnızca sağ tık menüsü eklentisi (`src/ZdpShell`) için gerekir.

1. Depoyu klonla ve `ZeroDataPeek.sln` dosyasını aç.
2. Platformu **x64** (ya da ARM64) seç; başlangıç profili **ZeroDataPeek (Package)** olsun.
3. `F5`.

Her gönderimde GitHub Actions x64 ve ARM64 için derler (`.github/workflows/build.yml`).

## Yapı

```
src/ZdpShell/         Dosya Gezgini sağ tık menüsü (C++)
src/ZeroDataPeek/     WinUI 3 uygulaması
  Assets/monaco/      Kod düzenleyici sayfası (Monaco)
  Assets/docedit/     Belge düzenleyici sayfası (zengin metin, DOCX okuma ve yazma)
  Models/             Sekme modeli
  Services/           Veri motoru (DuckDB), ayarlar, dil, dosya türleri, metin okuma/yazma
  Views/              Veri sekmesi, düzenleyiciler, karşılaştırma, dosyalarda arama, diyaloglar
  Controls/           Veri tablosu, panel ayırıcı
  Strings/en.json     İngilizce metinler
tools/                Çeviri denetimi
docs/                 Tasarım, yol haritası, yayımlama, mağaza sayfası metinleri
```

## Teşekkür

- [DuckDB](https://duckdb.org) (MIT) ve [DuckDB.NET](https://github.com/Giorgi/DuckDB.NET) (MIT): veri motoru
- [Monaco Editor](https://github.com/microsoft/monaco-editor) (MIT): kod düzenleyici
- [ExcelDataReader](https://github.com/ExcelDataReader/ExcelDataReader) (MIT): Excel okuma
- [JSZip](https://github.com/Stuk/jszip) (MIT): DOCX paketleme
- [SQL Formatter](https://github.com/sql-formatter-org/sql-formatter) (MIT): SQL biçimlendirme
- [Marked](https://github.com/markedjs/marked) (MIT): Markdown önizleme

## Lisans

[MIT](LICENSE) © Ali Apaydın

## Gizlilik

Uygulama veri toplamaz ve internete bağlanmaz. Ayrıntılar: [PRIVACY.md](PRIVACY.md)
