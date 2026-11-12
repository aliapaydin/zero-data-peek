# Zero Data Peek: çalışma notları

- WinUI 3 / Windows App SDK 2.x / .NET 10, tek proje MSIX: `src/ZeroDataPeek`. Görsel dil Zero File Commander ve Zero File Extractor ile ortak (Mica, kart yüzeyler, Zero Mavi + Sinyal Amber).
- Hedef platformlar x64 ve ARM64. Derleme yalnızca Windows'ta yapılabiliyor; Linux ortamında doğrulama GitHub Actions (`.github/workflows/build.yml`) üzerinden. Çalışma zamanı davranışı CI'da sınanmıyor; arayüz değişikliklerini Ali kendi makinesinde dener.
- Sekmelerin tek doğruluk kaynağı `MainWindow._tabs`. Yatay şerit (`TabStrip`) ve dikey liste (`TabList`) ona göre eşitlenir (`SyncTabViews`, `QueueAdoptStripOrder`). Yeni sekme türü eklerken görünümü `DocumentTab.View` olarak ver.
- Her komut `MainWindow.BuildCommands` içinde tanımlı; menü öğeleri ve düğmeler `Tag` ile aynı kimliği çağırır, komut paleti listeyi oradan alır. Yeni özellik eklerken önce komutunu tanımla.
- Uygulama tek örnek çalışır (`Program.cs`); sonradan açılan dosyalar `App.OnRedirected` üzerinden çalışan pencereye gelir.
- Metin dosyaları: kodlama ve satır sonu `Services/TextFileIO.cs` içinde algılanır ve kaydederken korunur. İkili dosyalar metin olarak açılmaz.
- Veri modu: `Services/DataEngine.cs` tek bir DuckDB bağlantısı tutar (geçici dosyada veritabanı, çağrılar sıraya girer). Her veri sekmesi dosyasını adlandırılmış bir tabloya (Parquet: görünüm) alır; SQL sonucu ve arama/sıralama görünümü ayrı tablolara yazılır, böylece sayfalama `rowid` aralığıyla hızlı kalır. Değerler SQL tarafında `VARCHAR`'a çevrilip okunur. Eklenti otomatik indirme kapalıdır (uygulama internete bağlanmaz); internet gerektiren DuckDB özelliği ekleme.
- `Controls/DataGridView.cs` kendi sanallaştırmasını yapar (hücre havuzu, 200 satırlık bloklar, satır satır kaydırma); ListView ya da DataGrid kullanmaz. Düzenleme yok, salt okunur.
- XAML'de yalnızca var olduğundan emin olunan tema kaynaklarını kullan (eksik anahtar derlemede değil açılışta çöker); emin değilsen ZFX'te kullanılanlardan seç.
- Arayüz metinleri kodda Türkçe; İngilizce karşılıklar `Strings/en.json` (anahtar: Türkçe metin). Yeni metin eklersen oraya da ekle; `python3 tools/check-strings.py` eksikleri gösterir ve CI'da çalışır. Aynı Türkçe sözcük iki farklı İngilizce anlama geliyorsa Türkçesini ayrıştır ("Açık" / "Açık tema"). Kod ve tanımlayıcılar İngilizce.
- Yeni özellikleri `docs/ROADMAP.md` sırasıyla ekle ve tamamlananları işaretle.
