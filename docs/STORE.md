# Mağaza sayfası metinleri

Partner Center'daki "Store listings" alanlarına kopyalanacak metinler. Türkçe ve İngilizce iki ayrı liste olarak girilir.

## Ortak alanlar

| Alan | Değer |
|---|---|
| Kategori | Geliştirici araçları (alt kategori yok) ya da Üretkenlik |
| Fiyat | Ücretsiz |
| Yaş derecelendirmesi | Anket: şiddet, kumar, kullanıcılar arası iletişim, konum, satın alma yok → 3+ |
| Gizlilik politikası adresi | `https://github.com/aliapaydin/zero-data-peek/blob/main/PRIVACY.md` |
| Web sitesi | `https://github.com/aliapaydin/zero-data-peek` |
| Destek | `https://github.com/aliapaydin/zero-data-peek/issues` |
| Sistem gereksinimi | Windows 10 sürüm 2004 (19041) ya da üstü; x64 ya da ARM64 |

## Türkçe

**Ad:** Zero Data Peek

**Kısa açıklama** (en çok 100 karakter önerilir):
Veri, kod ve belge dosyalarını tek pencerede açan sekmeli görüntüleyici ve düzenleyici.

**Açıklama:**

Bir CSV'ye, Parquet dosyasına, Python betiğine ya da Word belgesine bakmak için dört ayrı program açma. Zero Data Peek hepsini sekmelerde açar.

VERİ
• CSV, TSV, Parquet, JSON, JSON Lines ve Excel dosyalarını tablo olarak açar; milyonlarca satırda akıcı kaydırma
• Gömülü SQL (DuckDB): açık dosyaları sorgula, iki dosyayı birbirine join'le
• Koşullu süzgeçler, sıralama, tüm kolonlarda arama, kolon profili (boş oranı, tekil değer, en sık değerler)
• CSV, Parquet, JSON ya da Excel olarak dışa aktarma: biçimler arası dönüştürme

KOD VE METİN
• 90 dile sözdizimi renklendirme (Python, SQL, DAX, Power Query, C#, JSON, YAML…)
• Bul ve değiştir, kod katlama, çoklu imleç, küçük harita
• SQL, JSON ve XML biçimlendirme; Markdown önizleme
• Dosya karşılaştırma: iki dosyanın farkı yan yana ya da satır içi
• Dosyalarda arama: bir klasördeki tüm dosyalarda, düzenli ifade desteğiyle
• Kodlama ve satır sonu algılanır, kaydederken korunur

BELGE
• Word (DOCX) belgelerini aç, düzenle, kaydet
• PDF, HTML ya da düz metin olarak dışa aktar, yazdır

ÇALIŞMA DÜZENİ
• Yatay ya da dikey sekmeler, bölünmüş görünüm (iki sekme yan yana)
• Dosya gezgini paneli: sürücüler, klasör ağacı, dosya işlemleri
• Oturumlar: açık sekmeleri .zdp dosyasına kaydet, tek tıkla geri aç
• Kaydedilmemiş sekmeler kapanışta saklanır
• Kompakt mod, açık ve koyu tema, komut paleti
• Dosya Gezgini sağ tık menüsü: "Zero Data Peek ile aç"

GİZLİLİK
Açık kaynaklıdır (MIT). Reklam, telemetri ve hesap yok; uygulama internete bağlanmaz. Dosyaların bilgisayarından çıkmaz.

**Öne çıkan özellikler** (her biri ayrı satır, en çok 20):
- CSV, Parquet, JSON ve Excel dosyalarını tablo olarak açar
- Gömülü SQL ile dosyaları sorgular ve birleştirir
- 90 dile sözdizimi renklendirmeli kod düzenleyici
- Word belgelerini açar, düzenler, PDF'e aktarır
- Dosya karşılaştırma ve dosyalarda arama
- Bölünmüş görünüm, yatay ve dikey sekmeler
- Oturumlar: açık sekmeleri kaydet, geri aç
- İnternete bağlanmaz, veri toplamaz

**Arama terimleri** (en çok 7): csv görüntüleyici, parquet, sql, json, kod düzenleyici, excel, dosya karşılaştırma

## English

**Name:** Zero Data Peek

**Short description:**
A tabbed viewer and editor that opens data, code and document files in one window.

**Description:**

Stop opening four different programs to look at a CSV, a Parquet file, a Python script and a Word document. Zero Data Peek opens them all in tabs.

DATA
• Opens CSV, TSV, Parquet, JSON, JSON Lines and Excel files as tables; smooth scrolling through millions of rows
• Built-in SQL (DuckDB): query open files and join two files together
• Conditional filters, sorting, search across all columns, column profile (null ratio, distinct values, most frequent values)
• Export to CSV, Parquet, JSON or Excel: convert between formats

CODE AND TEXT
• Syntax highlighting for 90 languages (Python, SQL, DAX, Power Query, C#, JSON, YAML…)
• Find and replace, code folding, multiple cursors, minimap
• Format SQL, JSON and XML; preview Markdown
• Compare files: differences side by side or inline
• Find in files: search every file in a folder, with regular expressions
• Encoding and line endings are detected and preserved on save

DOCUMENTS
• Open, edit and save Word (DOCX) documents
• Export to PDF, HTML or plain text; print

WORKSPACE
• Horizontal or vertical tabs, split view (two tabs side by side)
• File explorer panel: drives, folder tree, file operations
• Sessions: save open tabs to a .zdp file and reopen them in one click
• Unsaved tabs are kept when you close the app
• Compact mode, light and dark themes, command palette
• File Explorer right-click menu: "Open with Zero Data Peek"

PRIVACY
Open source (MIT). No ads, no telemetry, no account; the app never connects to the internet. Your files stay on your computer.

**Product features:**
- Opens CSV, Parquet, JSON and Excel files as tables
- Queries and joins files with built-in SQL
- Code editor with syntax highlighting for 90 languages
- Opens and edits Word documents, exports to PDF
- File comparison and find in files
- Split view, horizontal and vertical tabs
- Sessions: save and reopen your open tabs
- Never connects to the internet, collects no data

**Search terms:** csv viewer, parquet viewer, sql, json, code editor, excel, file compare

## Ekran görüntüleri

En az 1, en çok 10; önerilen 1920×1080 ya da 2560×1440 PNG. Pencereyi tam boyuta yakın tut, koyu ve açık temayı karıştır. Önerilen sıra:

1. Veri sekmesi: büyükçe bir CSV ya da Parquet, SQL paneli açık ve bir sorgu sonucu görünür
2. Kod düzenleyici: renklendirilmiş bir Python ya da SQL dosyası, solda gezgin paneli
3. Bölünmüş görünüm: solda SQL dosyası, sağda tablo
4. Dosya karşılaştırma: iki sürüm yan yana
5. Belge modu: biçimli bir Word belgesi
6. Dosyalarda arama sonuçları
7. Kolon profili paneli açık bir tablo
8. Kompakt mod + dikey sekmeler (koyu tema)

Kişisel ya da şirkete ait veri içeren dosyaları ekran görüntüsünde kullanma; örnek veri kullan.

## Sertifikasyon notları ("Notes for certification")

```
Zero Data Peek is a desktop (full trust) file viewer and editor. No account or sign-in is needed; all features are available immediately.

runFullTrust: required because this is a packaged Win32/WinUI 3 desktop app that reads and writes files the user opens.

The package contains a File Explorer context menu extension (IExplorerCommand, ZdpShell.dll, COM surrogate server). It only reads the selected file paths and passes them to the app on the command line. The user chooses in Settings which file types show the menu entry.

The app does not connect to the internet and collects no data. The embedded DuckDB engine has extension auto-download disabled. Web content shown in WebView2 (code editor, document editor) is loaded only from local files inside the package.

To test: open any CSV, JSON, text or DOCX file with File > Open (Ctrl+O), or drag files onto the window. The interface language follows Windows (Turkish or English) and can be changed in Settings.
```
