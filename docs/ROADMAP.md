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
- [ ] CSV / TSV: ayraç, kodlama ve başlık satırı algılama; sanal kaydırmalı tablo
- [ ] Parquet, JSON / JSON Lines, Excel okuma (DuckDB)
- [ ] Kolon profili: tür, boş oranı, tekil değer sayısı, en küçük / en büyük, dağılım
- [ ] Sıralama, filtre, kolon gizleme ve dondurma, hücre ve seçim kopyalama
- [ ] SQL paneli: açık dosyaya ve sekmeler arasında sorgu (join)
- [ ] Dönüştürme: CSV, Parquet, JSON ve XLSX arasında "farklı kaydet"
- [ ] Tablo ile ham metin arasında geçiş

## v0.3: Kod modu
- [ ] Sözdizimi renklendirme (Scintilla / Lexilla tabanlı düzenleyici; yedek plan Monaco)
- [ ] Satır numaraları, kod katlama, çoklu imleç, girinti kılavuzları
- [ ] Bul ve değiştir (regex), satıra git
- [ ] Büyük dosyalar; diskte değişen dosyayı fark edip yeniden yükleme
- [ ] Kaydedilmemiş sekmeleri kapanışta saklama

## v0.4: Belge modu
- [ ] Zengin metin: yazı tipi, kalın / italik / altı çizili, renk, hizalama, liste, tablo, resim
- [ ] RTF ve DOCX açma ve kaydetme; PDF ve HTML olarak dışa aktarma
- [ ] Markdown önizleme
- [ ] Yazdırma

## v0.5: .zdp çalışma paketi
- [ ] Tek dosyada veri anlık görüntüsü, SQL sorguları, filtreler, kolon biçimleri, notlar ve açık sekmeler
- [ ] `.zdp` dosya ilişkilendirmesi ve simgesi
- [ ] Bölünmüş görünüm, dosya karşılaştırma
- [ ] Zero File Commander ile bütünleşme (önizleme)
