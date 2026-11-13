# Yol haritası

## v0.1: Kabuk
- [x] WinUI 3 + Windows App SDK + .NET 10, MSIX paketleme, x64 ve ARM64
- [x] Zero ailesiyle ortak görsel dil: Mica, menü, kart yüzeyler
- [x] Yatay sekmeler (başlık çubuğunda) ve dikey sekmeler (solda liste); sürükleyerek sıralama
- [x] Düz metin düzenleyici: kodlama ve satır sonu algılama, koruyarak kaydetme, yakınlaştırma, sözcük kaydırma
- [x] Tek örnek: sonradan açılan dosyalar çalışan pencereye sekme olarak gelir
- [x] Oturumu geri yükleme, son açılanlar, sürükle bırak ile açma
- [x] Komut paleti, klavye kısayolları
- [x] Açık / koyu / sistem teması, Türkçe / İngilizce arayüz
- [x] Karşılama sayfası, durum çubuğu, ayarlar

## v0.2: Veri modu
- [x] Gömülü DuckDB; tüm veri sekmeleri tek veritabanını paylaşır, geçici dosyada durur
- [x] CSV / TSV: ayraç, başlık ve tür algılama; UTF-8 olmayan dosyaları çevirerek okuma; düzensiz dosyalarda hoşgörülü okuma
- [x] Parquet (içeri kopyalamadan), JSON / JSON Lines
- [x] Sanal tablo: satır sayısından bağımsız kaydırma, kolon boyutlandırma, klavyeyle gezinme, hücre ve satır kopyalama
- [x] Başlığa tıklayarak sıralama, tüm kolonlarda arama
- [x] Kolon profili: dolu / boş, tekil, en küçük / en büyük, ortalama, ortanca, en sık değerler
- [x] SQL paneli: açık dosyaya ve sekmeler arasında sorgu (join); tek tıkla kolon özeti (SUMMARIZE)
- [x] Dışa aktarma: CSV, TSV, Parquet, JSON, JSON Lines
- [x] Tablo ile ham metin arasında geçiş
- [x] Excel (XLSX, XLS) okuma, sayfa seçimi
- [x] Noktalı virgülle ayrılmış dosyalarda ondalık virgülü tanıma
- [x] Sağ tık menüsü: değere göre süzme ve hariç tutma, sıralama, kopyalama
- [ ] Excel (XLSX) olarak dışa aktarma
- [ ] Koşullu süzgeçler (büyük, küçük, içerir), kolon gizleme ve dondurma, çoklu hücre seçimi
- [ ] Uzun sorguları iptal etme, sorgu geçmişi
- [ ] CSV açılış seçenekleri (ayraç, kodlama, başlık satırı) için diyalog

## v0.3: Kod modu ve gezgin
- [x] Kod düzenleyici: WebView2 içinde Monaco, yerel dosyalardan (internet yok); açılamazsa düz metin kutusuna düşer
- [x] 90 dile sözdizimi renklendirme (Python, SQL, DAX, Power Query, C#, JSON, YAML…), dosya adından dil seçimi
- [x] Bul ve değiştir (regex), satıra git, kod katlama, çoklu imleç, küçük harita
- [x] Açık / koyu temaya uyan düzenleyici teması; yazı tipi, boyut, sözcük kaydırma ayarları
- [x] Dosya gezgini paneli: klasör ağacı, açılamayan dosyalar soluk; ilk açılan dosyanın klasörünü izler
- [ ] Gezginde açık dosyayı vurgulama, klasör değişikliklerini izleme, dosya adına göre süzme
- [ ] Diskte değişen dosyayı fark edip yeniden yükleme
- [ ] Kaydedilmemiş sekmeleri kapanışta saklama
- [ ] Düzenleyici arayüzünü Türkçeleştirme (bul kutusu, sağ tık menüsü şu an İngilizce)

## v0.4: Belge modu, oturumlar, sağ tık menüsü
- [x] Belge düzenleyici: A4 görünümlü zengin metin (WebView2), araç çubuğu: stil, yazı tipi, boyut, kalın / italik / altı çizili / üstü çizili, renk ve vurgu, hizalama, listeler, girinti, bağlantı, resim, tablo
- [x] Word (DOCX) açma ve kaydetme: paragraf, başlık, liste, tablo, resim, bağlantı ve karakter biçimleri; taşınamayan öğeler (üstbilgi, dipnot, yorum…) için üzerine kaydetmeden önce uyarı
- [x] PDF, HTML ve düz metin olarak dışa aktarma; yazdırma
- [x] Adlandırılmış oturumlar: açık sekmeleri `.zdp` dosyasına kaydetme, açma, son oturumlar; `.zdp` çift tıklanınca oturum açılır
- [x] Dosya Gezgini sağ tık menüsü: "Zero Data Peek ile aç"; hangi türlerde görüneceği Ayarlar'dan seçilir
- [ ] RTF ve ODT açma; Markdown önizleme
- [ ] Tabloda satır / sütun ekleme ve silme, hücre birleştirme; resim boyutlandırma tutamaçları
- [ ] Üstbilgi / altbilgi, sayfa numarası, sayfa kenar boşluğu ayarı
- [ ] Belge düzenleyicide bul ve değiştir

## v0.5: .zdp çalışma paketi
- [ ] Oturuma veri sekmelerinin SQL sorgularını, süzgeçlerini ve kolon genişliklerini de kaydetme
- [ ] Veri anlık görüntüsünü oturum dosyasına gömme (dosyayı başkasına gönderince analiz aynen açılsın)
- [ ] `.zdp` için ayrı dosya simgesi
- [ ] Bölünmüş görünüm, dosya karşılaştırma
- [ ] Zero File Commander ile bütünleşme (önizleme)
- [ ] Store paketi: DuckDB'nin yalnızca hedef platform kitaplığını paketleme (boyut)
