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
- [x] Excel (XLSX) olarak dışa aktarma
- [x] Koşullu süzgeçler: içerir, ile başlar / biter, büyük, küçük, arasında, boş; tek tek kaldırma
- [ ] Kolon gizleme ve dondurma, çoklu hücre seçimi
- [x] Sorgu geçmişi
- [ ] Uzun sorguları iptal etme
- [x] CSV okuma seçenekleri: ayraç, ondalık ayırıcı, başlık satırı, kodlama, tüm kolonları metin olarak alma

## v0.3: Kod modu ve gezgin
- [x] Kod düzenleyici: WebView2 içinde Monaco, yerel dosyalardan (internet yok); açılamazsa düz metin kutusuna düşer
- [x] 90 dile sözdizimi renklendirme (Python, SQL, DAX, Power Query, C#, JSON, YAML…), dosya adından dil seçimi
- [x] Bul ve değiştir (regex), satıra git, kod katlama, çoklu imleç, küçük harita
- [x] Açık / koyu temaya uyan düzenleyici teması; yazı tipi, boyut, sözcük kaydırma ayarları
- [x] Dosya gezgini paneli: klasör ağacı, açılamayan dosyalar soluk; ilk açılan dosyanın klasörünü izler
- [x] Gezginde sürücü ve sık kullanılan klasör seçimi, açık dosyayı vurgulama, dosya adına göre süzme
- [x] Gezginde sağ tık: yeni dosya / klasör, yeniden adlandırma, Geri Dönüşüm Kutusu'na gönderme, yol kopyalama
- [ ] Gezginde klasör değişikliklerini kendiliğinden izleme
- [x] Diskte değişen dosyayı fark edip yeniden yükleme
- [x] Kaydedilmemiş sekmeleri kapanışta saklama (ve yarım dakikada bir yedekleme)
- [x] Belgeyi biçimlendirme: SQL, JSON, XML ve Monaco'nun desteklediği diller
- [x] Markdown önizleme
- [ ] Düzenleyici arayüzünü Türkçeleştirme (bul kutusu, sağ tık menüsü şu an İngilizce)

## v0.4: Belge modu, oturumlar, sağ tık menüsü
- [x] Belge düzenleyici: A4 görünümlü zengin metin (WebView2), araç çubuğu: stil, yazı tipi, boyut, kalın / italik / altı çizili / üstü çizili, renk ve vurgu, hizalama, listeler, girinti, bağlantı, resim, tablo
- [x] Word (DOCX) açma ve kaydetme: paragraf, başlık, liste, tablo, resim, bağlantı ve karakter biçimleri; taşınamayan öğeler (üstbilgi, dipnot, yorum…) için üzerine kaydetmeden önce uyarı
- [x] PDF, HTML ve düz metin olarak dışa aktarma; yazdırma
- [x] Adlandırılmış oturumlar: açık sekmeleri `.zdp` dosyasına kaydetme, açma, son oturumlar; `.zdp` çift tıklanınca oturum açılır
- [x] Dosya Gezgini sağ tık menüsü: "Zero Data Peek ile aç"; hangi türlerde görüneceği Ayarlar'dan seçilir
- [ ] RTF ve ODT açma
- [ ] Tabloda satır / sütun ekleme ve silme, hücre birleştirme; resim boyutlandırma tutamaçları
- [ ] Üstbilgi / altbilgi, sayfa numarası, sayfa kenar boşluğu ayarı
- [ ] Belge düzenleyicide bul ve değiştir

## v1.0: Günlük kullanım (ilk mağaza sürümü)
- [x] Rahatsız etmeyen bildirimler: kaydetme, dışa aktarma ve dosya işlemleri durum çubuğunda birkaç saniye görünür
- [x] Sekme sağ tık menüsü: kaydet, farklı kaydet, çoğalt, yolu kopyala, gezginde göster, diğerlerini / sağdakileri / tümünü kapat
- [x] Kompakt mod: alçak satırlar, küçük yazılar, dar boşluklar
- [x] Geniş, bölümlere ayrılmış ayarlar penceresi; ayrıntılı Hakkında sayfası
- [x] Oturuma veri sekmelerinin SQL sorgularını kaydetme
- [x] Bölünmüş görünüm: iki sekme yan yana
- [x] Dosya karşılaştırma (yan yana / satır içi)
- [x] Dosyalarda arama: büyük/küçük harf, tam sözcük, düzenli ifade, dosya adı süzgeçleri
- [ ] Hızlı grafik (histogram, çubuk)

## v1.1: .zdp çalışma paketi
- [ ] Oturuma süzgeçleri ve kolon genişliklerini de kaydetme
- [ ] Veri anlık görüntüsünü oturum dosyasına gömme (dosyayı başkasına gönderince analiz aynen açılsın)
- [ ] `.zdp` için ayrı dosya simgesi
- [ ] Zero File Commander ile bütünleşme (önizleme)
- [x] Store paketi: yalnızca hedef platformun yerel kitaplıkları (paket 169 MB → 43 MB)
- [ ] Kullanılmayan Windows ML kitaplıklarını (onnxruntime, DirectML) paketten çıkarma
